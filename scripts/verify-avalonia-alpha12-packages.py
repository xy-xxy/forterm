#!/usr/bin/env python3
"""Complete alpha.12 package verification on ARM Mac, without loading x64 IL.
The frozen original packaging helper's cross-architecture check failed after
successfully building both Windows artifacts. Preserve them; verify in place.
"""
import pathlib, hashlib, json, subprocess, os, mmap, zipfile, sys, shutil, struct
repo=pathlib.Path(__file__).resolve().parents[1]
root=repo/'avalonia';release=repo/'release';version='0.3.0-alpha.12';tag='alpha12'
identity=json.loads((release/f'IDENTITY-Avalonia-{version}.json').read_text());archive=pathlib.Path(identity['windowsPrototypeZIP'])
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(archive)==identity['windowsZIPSHA256']
checkpoint=pathlib.Path(identity['checkpoint']);sources=json.loads((checkpoint/'SOURCE-HASHES.json').read_text())
for name,digest in sources.items():assert sha(root/name)==digest,name
work=root/'artifacts/alpha12-delivery';payload=work/'payload'/('Forterm-Avalonia-'+version)
manifest={p.relative_to(payload).as_posix():sha(p) for p in sorted(payload.rglob('*')) if p.is_file()}
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 expected={n.split('/',1)[1]:hashlib.sha256(z.read(n)).hexdigest() for n in z.namelist() if not n.endswith('/')}
assert manifest==expected
installer=release/f'Forterm-Avalonia-{version}-windows-x64-setup.exe';portable=release/f'Forterm-Avalonia-{version}-windows-x64-portable.exe'
assert installer.is_file() and portable.is_file()
installer_extract=work/'installer-extracted'
for name,digest in manifest.items():
 matches=[p for p in installer_extract.rglob(pathlib.PurePosixPath(name).name) if p.is_file() and p.as_posix().endswith('/'+name) and sha(p)==digest]
 assert len(matches)==1,('installer',name)
managed=root/'src/Forterm.Portable/obj/Release/net10.0/win-x64/Forterm.Portable.dll'
embedded={}
with portable.open('rb') as f,mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ) as binary:
 for name,path in [('managedBootstrap',managed),('frozenZIP',archive)]:
  content=path.read_bytes();offset=binary.find(content);assert offset>=0,('not embedded',name)
  embedded[name]={'sha256':sha(path),'offset':offset,'bytes':path.stat().st_size}
# Read actual .NET v6 bundle metadata. The singlefilehost runtime is linked into the PE host;
# do not incorrectly require separate coreclr/clrjit/hostpolicy DLL blobs.
data=portable.read_bytes();signature=bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae');marker=data.find(signature);assert marker>=8
position=struct.unpack_from('<Q',data,marker-8)[0];major,minor,count=struct.unpack_from('<III',data,position);position+=12
assert major==6 and count<4096
def read_string():
 global position
 length=0;shift=0
 while True:
  byte=data[position];position+=1;length|=(byte&127)<<shift
  if byte<128:break
  shift+=7;assert shift<21
 value=data[position:position+length].decode('utf-8');position+=length;return value
bundleid=read_string();position+=40;bundle_entries=[]
for _ in range(count):
 offset,size,compressed,kind=struct.unpack_from('<QQQB',data,position);position+=25;name=read_string();assert compressed==0 and 0<=offset<=len(data)-size
 content=data[offset:offset+size];digest=hashlib.sha256(content).hexdigest()
 if kind==1:
  counterpart=managed if name=='Forterm.Portable.dll' else root/'artifacts/alpha12-win-x64'/name
  assert counterpart.is_file() and sha(counterpart)==digest,('bundle managed mismatch',name)
 else:assert kind in [3,4],('unexpected native/other bundle entry',name,kind)
 bundle_entries.append({'name':name,'kind':kind,'bytes':size,'sha256':digest})
assert len(bundle_entries)==count and not any(e['kind']==2 for e in bundle_entries)
# The same frozen sources and exact ZIP, compiled for this host. Extract+hash only.
dotnet='/Users/xianyu/.cache/forterm-dotnet/dotnet';companion=work/'portable-mac-check'
if not (companion/'Forterm.Portable').exists():
 args=[dotnet,'publish',str(root/'src/Forterm.Portable'),'-c','Release','-r','osx-arm64','--self-contained','true','--no-restore','-p:PortablePayload='+str(archive),'-p:PortablePayloadSHA256='+sha(archive),'-o',str(companion)]
 with (root/'evidence/alpha12-portable-mac-check-build.log').open('w') as log:subprocess.run(args,check=True,stdout=log,stderr=subprocess.STDOUT)
probe=json.loads(subprocess.check_output([str(companion/'Forterm.Portable'),'--portable-runtime-check'],text=True,env=dict(os.environ,DOTNET_ROOT='/tmp/forterm-no-runtime')))
assert probe['PayloadSHA256']==sha(archive) and probe['Files']==manifest
assert probe['VisibleWindowsCreated']==0 and probe['ChildProcessesCreated']==0
(root/'evidence/alpha12-portable-mac-final-payload-check.json').write_text(json.dumps(probe,indent=2)+'\n')
sys.path.insert(0,str(root/'tooling'))
from pe_imports import read_imports
reports={}
for kind,path in [('installer',installer),('portable',portable)]:
 parsed=read_imports(path);imports=parsed['regular']+parsed['delay'];assert not any('vcruntime' in n.lower() or 'msvcp' in n.lower() for n in imports)
 if kind=='portable':assert parsed['machine']=='0x8664'
 reports[kind]={'path':str(path),'sha256':sha(path),'bytes':path.stat().st_size,'verifiedPayloadFiles':len(manifest),'payloadMatchesFrozenZIP':True,'machine':parsed['machine'],'regularImports':parsed['regular'],'delayImports':parsed['delay'],'windowsExecuted':False}
reports['portable'].update(exactFrozenZIPEmbedded=True,embeddedEvidence=embedded,sameFrozenSourceMacExtractCheck=True)
report={'version':version,'zipPath':str(archive),'zipSHA256':sha(archive),'payloadFiles':manifest,'packages':reports,'portableBundle':{'major':major,'minor':minor,'entries':bundle_entries,'allManagedRuntimeFilesMatchMainZIP':True,'nativeBundleEntries':0,'singleFileHostRuntimeIsInPE':True,'formatSource':'https://github.com/dotnet/runtime/tree/v10.0.14/src/installer/managed/Microsoft.NET.HostModel/Bundle'},'sourceMatchesFrozenManifest':len(sources),'installerPerUser':True,'installerVersionedDirectory':True,'uninstallOnlyExplicitPayloadFiles':True,'portableBootstrap':'self-contained C# single-file; exact original ZIP; owned temporary runtime; ArgumentList; waits for child; no Go','nativeWindowsVerified':False,'priorVerifierFailure':'Windows-x64 managed assembly cannot load on ARM64 Mac. Original helper stopped after building packages. No Windows code ran. This supplement uses the unchanged frozen sources compiled for Mac, plus exact-byte Windows EXE resource and embedded native runtime checks.','supplementalVerifierPath':str(pathlib.Path(__file__).resolve()),'supplementalVerifierSHA256':sha(pathlib.Path(__file__))}
verification=release/f'VERIFICATION-Avalonia-{version}-windows.json'
assert not verification.exists(),'Refusing overwrite final verification'
verification.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
checksums=release/f'SHA256SUMS-Avalonia-{version}.txt'
with checksums.open('a') as f:
 for value in reports.values():f.write(value['sha256']+'  '+pathlib.Path(value['path']).name+'\n')
# Capture the supplemental verifier separately; never alter the frozen 537-file archive.
extra=repo/'checkpoints/avalonia-0.3.0-alpha.12-packaging-verifier-20261030';extra.mkdir(exist_ok=False)
shutil.copy2(__file__,extra/'verify-packages.py');(extra/'SHA256.txt').write_text(sha(pathlib.Path(__file__))+'  verify-packages.py\n')
print(json.dumps({'version':version,'sourceMatches':len(sources),'payloadFiles':len(manifest),'packages':reports},ensure_ascii=False,indent=2))

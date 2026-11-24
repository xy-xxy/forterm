#!/usr/bin/env python3
"""Freeze a local alpha checkpoint; never replace an existing candidate."""
import pathlib,hashlib,json,shutil,tarfile,zipfile,plistlib,subprocess,sys
root=pathlib.Path(__file__).resolve().parents[1];repo=root.parent;release=repo/'release';version=sys.argv[1] if len(sys.argv)>1 else '0.3.0-alpha.33';tag='alpha'+version.rsplit('.',1)[1]
cp=repo/'checkpoints'/('avalonia-'+version+'-20261111');app=release/('Forterm-Avalonia-'+version+'-macos-arm64.app');zip_path=release/('Forterm-Avalonia-'+version+'-windows-x64.zip')
audit=json.loads((root/'evidence/native-imports.json').read_text());assert audit['version']==version and not audit['unresolved'];assert all(e['objdumpExit']==0 and e['peParsed'] for e in audit['regularAndDelayImportTableEvidence'])
for p in [cp,app,zip_path]:
 if p.exists():raise SystemExit('Refusing overwrite '+str(p))
cp.mkdir();mac=app/'Contents/MacOS';mac.mkdir(parents=True)
shutil.copytree(root/('artifacts/'+tag+'-osx-arm64'),mac,dirs_exist_ok=True,ignore=shutil.ignore_patterns('*.pdb','*.PDB'));shutil.copytree(root/'licenses',app/'Contents/Resources/licenses')
info={'CFBundleName':'Forterm Avalonia '+tag,'CFBundleDisplayName':'Forterm Avalonia '+tag,'CFBundleIdentifier':'cn.forterm.avalonia.'+tag,'CFBundleExecutable':'Forterm','CFBundlePackageType':'APPL','CFBundleShortVersionString':'0.3.0','CFBundleVersion':version.rsplit('.',1)[1],'FortermBuildVersion':version,'LSMinimumSystemVersion':'14.0','NSHighResolutionCapable':True}
(app/'Contents/Info.plist').write_bytes(plistlib.dumps(info));(app/'Contents/PkgInfo').write_text('APPL????')
subprocess.run(['codesign','--force','--deep','--sign','-',str(app)],check=True);subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
sources={}
for path in sorted(root.rglob('*')):
 rel=path.relative_to(root)
 if not path.is_file() or any(part in {'bin','obj','artifacts','evidence','fixture-venv','.git','__pycache__'} for part in rel.parts):continue
 sources[rel.as_posix()]=sha(path)
(cp/'SOURCE-HASHES.json').write_text(json.dumps(sources,indent=2)+'\n')
with tarfile.open(cp/'avalonia-source.tar.gz','w:gz') as tar:
 for name in sources:tar.add(root/name,arcname='avalonia/'+name,recursive=False)
for name in ['Forterm.exe','xshell.exe','xftp.exe','forterm-diagnose.exe']:
 header=subprocess.check_output(['x86_64-w64-mingw32-objdump','-f',str(root/('artifacts/'+tag+'-win-x64')/name)],text=True)
 assert 'pei-x86-64' in header,name
with zipfile.ZipFile(zip_path,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
 base='Forterm-Avalonia-'+version
 for path in sorted((root/('artifacts/'+tag+'-win-x64')).rglob('*')):
  if path.is_file() and path.suffix.lower()!='.pdb':z.write(path,base+'/'+path.relative_to(root/('artifacts/'+tag+'-win-x64')).as_posix())
 for path in sorted((root/'licenses').rglob('*')):
  if path.is_file():z.write(path,base+'/licenses/'+path.relative_to(root/'licenses').as_posix())
 z.write(root/'README.md',base+'/README.md');z.write(root/'docs/MIGRATION.md',base+'/MIGRATION.md')
 for name in ['RELEASE-NOTES.md','WINDOWS-ACCEPTANCE.md']:
  z.write(root/'docs'/name,base+'/'+name)
with zipfile.ZipFile(zip_path) as z:
 assert z.testzip() is None
 assert not any(name.lower().endswith('.pdb') for name in z.namelist())
 assert all(any(name.endswith('/'+entry) for name in z.namelist()) for entry in ['Forterm.exe','xshell.exe','xftp.exe','forterm-diagnose.exe'])
assert not any(p.suffix.lower()=='.pdb' for p in app.rglob('*') if p.is_file())
symbols=release/('Forterm-Avalonia-'+version+'-symbols.zip')
if symbols.exists():raise SystemExit('Refusing overwrite '+str(symbols))
with zipfile.ZipFile(symbols,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
 for rid in ['win-x64','osx-arm64']:
  for path in sorted((root/('artifacts/'+tag+'-'+rid)).rglob('*')):
   if path.is_file() and path.suffix.lower()=='.pdb':z.write(path,rid+'/'+path.relative_to(root/('artifacts/'+tag+'-'+rid)).as_posix())
 for path in sorted((root/'licenses').rglob('*')):
  if path.is_file():z.write(path,'licenses/'+path.relative_to(root/'licenses').as_posix())

with tarfile.open(cp/'avalonia-source.tar.gz') as tar:
 for name,digest in sources.items():assert hashlib.sha256(tar.extractfile('avalonia/'+name).read()).hexdigest()==digest
identity={'version':version,'stage':'delivery candidate; external Windows/native verification pending','sourceFiles':len(sources),'sourceManifestSHA256':sha(cp/'SOURCE-HASHES.json'),'sourceArchiveSHA256':sha(cp/'avalonia-source.tar.gz'),'checkpoint':str(cp),'macApp':str(app),'macExecutableSHA256':sha(mac/'Forterm'),'macGUIVerified':False,'macAdhocSignatureVerified':True,'windowsPrototypeZIP':str(zip_path),'windowsZIPSHA256':sha(zip_path),'windowsMainExeSHA256':sha(root/('artifacts/'+tag+'-win-x64/Forterm.exe')),'windowsRuntimeVerified':False,'fourLaunchersMigrated':True,'installerPortableGenerated':False,'normalPackageContainsPdb':False,'symbolsArchive':str(symbols),'symbolsSHA256':sha(symbols),'normalMacBytes':sum(p.stat().st_size for p in app.rglob('*') if p.is_file()),'normalWindowsZIPBytes':zip_path.stat().st_size,'normalWindowsExpandedBytes':sum(p.file_size for p in zipfile.ZipFile(zip_path).infolist()),'normalAppInvokesGo':False,'tests':(root/('evidence/'+tag+'-final-validation.log')).read_text().strip().splitlines()[-1],'runtimeCheck':'native Skia/HarfBuzz on self-contained Mac without usable DOTNET_ROOT; no visible window','staticNativeImports':str(len(json.loads((root/'evidence/native-imports.json').read_text())['regularAndDelayImportTableEvidence']))+' PE incl subfolders; no unresolved static imports or vcruntime/msvcp; not clean Windows verification'}
portable_log=root/('evidence/'+tag+'-portable-tests.log')
if portable_log.exists():identity['portableTargetedTests']=portable_log.read_text().strip().splitlines()[-1]
(release/('IDENTITY-Avalonia-'+version+'.json')).write_text(json.dumps(identity,ensure_ascii=False,indent=2)+'\n');(cp/'BUILD-ID.json').write_text(json.dumps(identity,ensure_ascii=False,indent=2)+'\n')
(release/('SHA256SUMS-Avalonia-'+version+'.txt')).write_text(identity['macExecutableSHA256']+'  '+str((mac/'Forterm').relative_to(release))+'\n'+identity['windowsZIPSHA256']+'  '+zip_path.name+'\n'+identity['symbolsSHA256']+'  '+symbols.name+'\n')
print(json.dumps(identity,ensure_ascii=False,indent=2))

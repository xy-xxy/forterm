#!/usr/bin/env python3
"""Build new per-user installer and temporary portable bootstrap from one frozen ZIP.
No Windows process is executed here. Never replace an existing delivery."""
import pathlib, sys, json, zipfile, hashlib, subprocess, shutil, os, mmap, struct
root = pathlib.Path(__file__).resolve().parents[1]
release = root.parent / 'release'
version = sys.argv[1]
tag = 'alpha' + version.rsplit('.', 1)[1]
identity = json.loads((release / f'IDENTITY-Avalonia-{version}.json').read_text())
archive = pathlib.Path(identity['windowsPrototypeZIP'])
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(archive) == identity['windowsZIPSHA256']
installer = release / f'Forterm-Avalonia-{version}-windows-x64-setup.exe'
portable = release / f'Forterm-Avalonia-{version}-windows-x64-portable.exe'
work = root / 'artifacts' / (tag + '-delivery')
for path in [installer, portable, work]:
    if path.exists(): raise SystemExit('Refusing overwrite ' + str(path))
work.mkdir()
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    assert all(not n.startswith('/') and '..' not in pathlib.PurePosixPath(n).parts for n in z.namelist())
    z.extractall(work / 'payload')
payload = work / 'payload' / ('Forterm-Avalonia-' + version)
manifest = {p.relative_to(payload).as_posix(): sha(p) for p in sorted(payload.rglob('*')) if p.is_file()}
assert all(name in manifest for name in ['Forterm.exe', 'xshell.exe', 'xftp.exe', 'forterm-diagnose.exe'])
assert not any(p.lower().endswith('.pdb') for p in manifest)
# Explicit deletes preserve unknown user files and every external metadata/vault location.
escape = lambda s: s.replace('$', '$$').replace('"', '$\\"')
uninstall = work / 'uninstall-files.nsh'
lines = ['Delete "$INSTDIR\\' + escape(name.replace('/', '\\')) + '"' for name in manifest]
dirs = {str(pathlib.PurePosixPath(name).parent) for name in manifest}
for name in list(dirs): dirs.update(str(p) for p in pathlib.PurePosixPath(name).parents)
for name in sorted(dirs, key=lambda p: (-p.count('/'), -len(p))):
    if name != '.': lines.append('RMDir "$INSTDIR\\' + escape(name.replace('/', '\\')) + '"')
uninstall.write_text('\n'.join(lines) + '\n')
compiler = pathlib.Path(os.environ.get('FORTERM_MAKENSIS', '/Users/xianyu/.cache/forterm-build-tools/nsis-3.04-native-arm64/mac/makensis'))
sevenzip = pathlib.Path('/Users/xianyu/Library/Caches/electron-builder/7zip@1.0.0/7zip-darwin-arm64-fti70/bin/7zz')
assert compiler.is_file() and sevenzip.is_file()
toolchain = json.loads((root/'tooling/nsis-native-arm64-toolchain.json').read_text())
actual_compiler_version = subprocess.check_output([str(compiler), '-VERSION'], text=True).strip()
actual_compiler_sha = sha(compiler)
if 'FORTERM_MAKENSIS' not in os.environ:
    assert actual_compiler_sha == toolchain['compilerSHA256'] and actual_compiler_version == toolchain['compilerVersion']
actual_tools = {'version':version,'packagerSHA256':sha(pathlib.Path(__file__)),
    'compilerPath':str(compiler),'compilerVersion':actual_compiler_version,'compilerSHA256':actual_compiler_sha,
    'defaultRecordedToolchain':actual_compiler_sha==toolchain['compilerSHA256'],
    'recordedToolchain':toolchain,'buildScriptPatchSHA256':sha(root/'tooling/nsis-python3-buildscript-compat.patch'),
    'sevenZipPath':str(sevenzip),'sevenZipSHA256':sha(sevenzip)}
reports = {}
for kind, output in [('installer', installer)]:
    args = [str(compiler), '-V3', '-DAPP_VERSION=' + version, '-DAPP_NUMERIC=0.3.0.' + version.rsplit('.', 1)[1], '-DSOURCE_DIR=' + str(payload), '-DOUTPUT_FILE=' + str(output)]
    args.append('-DUNINSTALL_FILES=' + str(uninstall))
    args.append(str(root / 'packaging' / (kind + '.nsi')))
    with (root / 'evidence' / (tag + '-' + kind + '-build.log')).open('w') as log: subprocess.run(args, check=True, stdout=log, stderr=subprocess.STDOUT, env=dict(os.environ, NSISDIR=str(compiler.parent.parent)))
    extracted = work / (kind + '-extracted')
    with (root / 'evidence' / (tag + '-' + kind + '-extract.log')).open('w') as log: subprocess.run([str(sevenzip), 'x', str(output), '-o' + str(extracted), '-y'], check=True, stdout=log, stderr=subprocess.STDOUT)
    matches = {}
    for name, digest in manifest.items():
        candidates = [p for p in extracted.rglob(pathlib.PurePosixPath(name).name) if p.is_file() and (p.as_posix().endswith('/' + name))]
        found = [p for p in candidates if sha(p) == digest]
        if len(found) != 1: raise RuntimeError(f'{kind} {name}: matching payloads {len(found)}')
        matches[name] = digest
    header = subprocess.check_output(['x86_64-w64-mingw32-objdump', '-p', str(output)], text=True)
    imports = [line.split(':', 1)[1].strip() for line in header.splitlines() if 'DLL Name:' in line]
    assert not any('vcruntime' in n.lower() or 'msvcp' in n.lower() for n in imports)
    reports[kind] = {'path': str(output), 'sha256': sha(output), 'bytes': output.stat().st_size, 'verifiedPayloadFiles': len(matches), 'payloadMatchesFrozenZIP': True, 'bootstrapStaticImports': imports, 'windowsExecuted': False}
# The portable bootstrap is C# ArgumentList, avoiding NSIS's bounded parameter strings.
dotnet = '/Users/xianyu/.cache/forterm-dotnet/dotnet'
portable_out = work / 'portable-publish'
args = [dotnet, 'publish', str(root/'src/Forterm.Portable'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:PortablePayload='+str(archive), '-p:PortablePayloadSHA256='+sha(archive), '-o', str(portable_out)]
with (root/'evidence'/(tag+'-portable-build.log')).open('w') as log: subprocess.run(args, check=True, stdout=log, stderr=subprocess.STDOUT)
shutil.copy2(portable_out/'Forterm.Portable.exe', portable)
managed = root/'src/Forterm.Portable/obj/Release/net10.0/win-x64/Forterm.Portable.dll'
assert managed.is_file()
# Compression is explicitly off. Verify the entire probed managed assembly and exact frozen ZIP are in the actual EXE.
with portable.open('rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as binary:
    embedded = {}
    for name, path in [('managedBootstrap', managed), ('frozenZIP', archive)]:
        content = path.read_bytes(); offset = binary.find(content); assert offset >= 0, name
        assert hashlib.sha256(binary[offset:offset+path.stat().st_size]).hexdigest() == sha(path), name
        embedded[name] = {'sha256': sha(path), 'offset': offset, 'bytes': path.stat().st_size}
# Check the actual Windows bundle table and every managed runtime file against the main ZIP.
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
bundle_id=read_string();position+=40;bundle_entries=[]
for _ in range(count):
    offset,size,compressed,kind=struct.unpack_from('<QQQB',data,position);position+=25;name=read_string();assert compressed==0 and 0<=offset<=len(data)-size
    digest=hashlib.sha256(data[offset:offset+size]).hexdigest()
    if kind==1:
        counterpart=managed if name=='Forterm.Portable.dll' else root/('artifacts/'+tag+'-win-x64')/name
        assert counterpart.is_file() and sha(counterpart)==digest,('bundle managed mismatch',name)
    else:assert kind in [3,4],('unexpected native/other entry',name,kind)
    bundle_entries.append({'name':name,'kind':kind,'bytes':size,'sha256':digest})
# Never load x64 IL in the ARM process. Compile the unchanged source for this host;
# its diagnostic path only extracts/hashes the exact Windows ZIP and creates no child.
companion=work/'portable-mac-check'
args=[dotnet,'publish',str(root/'src/Forterm.Portable'),'-c','Release','-r','osx-arm64','--self-contained','true','--no-restore','-p:PortablePayload='+str(archive),'-p:PortablePayloadSHA256='+sha(archive),'-o',str(companion)]
with (root/'evidence'/(tag+'-portable-mac-check-build.log')).open('w') as log:subprocess.run(args,check=True,stdout=log,stderr=subprocess.STDOUT)
probe=json.loads(subprocess.check_output([str(companion/'Forterm.Portable'),'--portable-runtime-check'],text=True,env=dict(os.environ,DOTNET_ROOT='/tmp/forterm-no-runtime')))
assert probe['PayloadSHA256']==sha(archive) and probe['Files']==manifest
assert probe['VisibleWindowsCreated']==0 and probe['ChildProcessesCreated']==0
(root/'evidence'/(tag+'-portable-mac-final-payload-check.json')).write_text(json.dumps(probe,indent=2)+'\n')
header = subprocess.check_output(['x86_64-w64-mingw32-objdump','-p',str(portable)],text=True)
imports = [line.split(':',1)[1].strip() for line in header.splitlines() if 'DLL Name:' in line]
assert not any('vcruntime' in n.lower() or 'msvcp' in n.lower() for n in imports)
reports['portable'] = {'path':str(portable),'sha256':sha(portable),'bytes':portable.stat().st_size,'verifiedPayloadFiles':len(manifest),'payloadMatchesFrozenZIP':True,'exactFrozenZIPEmbedded':True,'embeddedEvidence':embedded,'managedMacExtractCheck':True,'bootstrapStaticImports':imports,'windowsExecuted':False}
report = {'version': version, 'zipPath': str(archive), 'zipSHA256': sha(archive), 'payloadFiles': manifest, 'packages': reports, 'portableBundle': {'major':major,'minor':minor,'entries':bundle_entries,'allManagedRuntimeFilesMatchMainZIP':True,'nativeBundleEntries':0,'formatSource':'https://github.com/dotnet/runtime/tree/v10.0.14/src/installer/managed/Microsoft.NET.HostModel/Bundle'}, 'installerPerUser': True, 'installerVersionedDirectory': True, 'uninstallOnlyExplicitPayloadFiles': True, 'portableBootstrap': 'self-contained C# single-file; exact frozen ZIP; owned temporary runtime; ArgumentList; waits for child; no Go', 'nativeWindowsVerified': False}
(release / f'VERIFICATION-Avalonia-{version}-windows.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
(release / f'BUILD-TOOLS-Avalonia-{version}.json').write_text(json.dumps(actual_tools, indent=2) + '\n')
checksums = release / f'SHA256SUMS-Avalonia-{version}.txt'
with checksums.open('a') as f:
    for value in reports.values(): f.write(value['sha256'] + '  ' + pathlib.Path(value['path']).name + '\n')
print(json.dumps({'version': version, 'payloadFiles': len(manifest), 'packages': reports}, ensure_ascii=False, indent=2))

#!/usr/bin/env python3
"""Run frozen alpha.15 packager with a separately documented ARM64 NSIS host.
Does not edit the frozen packager, application sources, original ZIP or old compiler.
"""
from pathlib import Path
import hashlib,json,subprocess,os,sys,shutil
repo=Path(__file__).resolve().parents[1];root=repo/'avalonia';version='0.3.0-alpha.15'
identity=json.loads((repo/'release'/f'IDENTITY-Avalonia-{version}.json').read_text());manifest=json.loads((Path(identity['checkpoint'])/'SOURCE-HASHES.json').read_text());packager=root/'tooling/package-windows.py'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(packager)==manifest['tooling/package-windows.py']
compiler=Path('/Users/xianyu/.cache/forterm-build-tools/nsis-3.04-native-arm64/mac/makensis');env=dict(os.environ,NSISDIR=str(compiler.parent.parent))
assert 'arm64' in subprocess.check_output(['file',str(compiler)],text=True)
assert subprocess.check_output([str(compiler),'-VERSION'],env=env,text=True).strip()=='v3.04'
receipt=json.loads((root/'evidence/alpha15-native-nsis-toolchain.json').read_text());assert receipt['compilerSHA256']==sha(compiler)
for name in ['Forterm-Avalonia-0.3.0-alpha.15-windows-x64-setup.exe','Forterm-Avalonia-0.3.0-alpha.15-windows-x64-portable.exe']:assert not (repo/'release'/name).exists(),'Refusing overwrite '+name
work=root/'artifacts/alpha15-delivery';archived=work.with_name('alpha15-delivery-old-x86-tool-unavailable')
if work.exists():assert not archived.exists();shutil.move(work,archived)
error=root/'evidence/alpha15-windows-packages-errors.log'
if error.exists():shutil.copy2(error,root/'evidence/alpha15-windows-x86-tool-error.log')
code=packager.read_text();old="compiler = pathlib.Path('/Users/xianyu/Library/Caches/electron-builder/nsis-3.0.4.1/nsis-3.0.4.1-1mx3n/mac/makensis')";assert code.count(old)==1
code=code.replace(old,'compiler = pathlib.Path('+repr(str(compiler))+')')
sys.argv=[str(packager),version]
exec(compile(code,str(packager)+'[native-host-override]','exec'),{'__file__':str(packager),'__name__':'__main__'})
extra=repo/'checkpoints/avalonia-0.3.0-alpha.15-native-packaging-20261031';extra.mkdir(exist_ok=False)
for src,name in [(Path(__file__),'native-nsis-runner.py'),(root/'evidence/alpha15-native-nsis-toolchain.json','toolchain.json'),(root/'evidence/alpha15-nsis-buildscript-compat.patch','buildscript-python3.patch'),(Path('/tmp/forterm-build-tool-restore-20261031/nsis-3.04-src.tar.bz2'),'nsis-3.04-src.tar.bz2')]:shutil.copy2(src,extra/name)
record={'version':version,'frozenPackagerSHA256':sha(packager),'supplementalRunnerSHA256':sha(Path(__file__)),'override':'host compiler path only; application/ZIP/NSIS script/Windows stub resources unchanged','compiler':receipt,'recoveryCheckpoint':str(extra),'filesSHA256':{p.name:sha(p) for p in extra.iterdir() if p.is_file()}}
(repo/'release'/f'BUILD-TOOLS-Avalonia-{version}.json').write_text(json.dumps(record,indent=2)+'\n');(extra/'SHA256.json').write_text(json.dumps(record['filesSHA256'],indent=2)+'\n')

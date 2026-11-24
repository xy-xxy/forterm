#!/usr/bin/env python3
"""Package the separately built native prototype without touching old releases."""
import hashlib, json, pathlib, shutil, subprocess, zipfile
ROOT=pathlib.Path(__file__).resolve().parents[1]
RELEASE=ROOT.parent/'release'
NAME='Forterm-Native-0.2.1-windows-x64'
OUT=RELEASE/NAME
ARCHIVE=RELEASE/(NAME+'.zip')
if OUT.exists() or ARCHIVE.exists():
 raise SystemExit('Refusing to overwrite an existing native release')
files=['forterm-native.exe','xshell.exe','xftp.exe','forterm-diagnose.exe']
for name in files:
 if not (ROOT/'evidence/release-0.2.1/windows'/name).is_file():raise SystemExit('Missing native build: '+name)
OUT.mkdir()
for name in files:shutil.copy2(ROOT/'evidence/release-0.2.1/windows'/name,OUT/name)
for name in ['README.md','MIGRATION.md','WINDOWS-ACCEPTANCE.md','VALIDATION.md','RELEASE-NOTES.md']:
 shutil.copy2(ROOT/name,OUT/name)
licenses=OUT/'licenses';licenses.mkdir()
shutil.copy2(ROOT.parent/'LICENSE',licenses/'Forterm-LICENSE')
modules=subprocess.check_output(['go','list','-m','-json','all'],cwd=ROOT,text=True)
decoder=json.JSONDecoder();pos=0
while pos<len(modules):
 while pos<len(modules) and modules[pos].isspace():pos+=1
 if pos==len(modules):break
 module,n=decoder.raw_decode(modules,pos);pos=n
 info=module.get('Replace',module);directory=info.get('Dir')
 if not directory:continue
 source=pathlib.Path(directory)
 matches=[p for p in source.iterdir() if p.is_file() and (p.name.upper().startswith(('LICENSE','LICENCE','COPYING','NOTICE','AUTHORS')))]
 if not matches:continue
 target=licenses/module['Path'].replace('/','_');target.mkdir(exist_ok=True)
 for p in matches:shutil.copy2(p,target/p.name)
evidence=OUT/'evidence';evidence.mkdir()
for name in ['system-vault-smoke.json','library-system-vault-gui.json','account-totp-gui.json','clipboard-native-gui.json','settings-race.log','totp-parity-race.log','auth-jump-race.log','agent-heartbeat-race.log','stream-cancel-race.log','fileops-race.log','legacy-probe-readiness-race.log','palette-override.log','account-totp-gui.json','account-vault-race.log','clipboard-native-gui.json','modifier-directed.log']:
 shutil.copy2(ROOT/'evidence'/name,evidence/name)
for folder in ['terminal-split','multi-source']:
 shutil.copytree(ROOT/'evidence'/folder,evidence/folder)
for name in ['file-session-association-20261027.log','file-session-association-race-20261027.log']:
 shutil.copy2(ROOT/'evidence'/name,evidence/name)
shutil.copy2(ROOT/'evidence/layout-native/nav-actual-result.json',evidence/'nav-actual-result.json')
for folder in ['small-0.2.1','regular-0.2.1','input-probe-0.2.1']:
 target=evidence/folder;target.mkdir()
 for source in (ROOT/'evidence'/folder).iterdir():
  if source.is_file() and source.suffix in ['.json','.png','.jpg','.log']:shutil.copy2(source,target/source.name)
for name in ['small-layout-0.2.1-directed.log','small-layout-0.2.1-race.log']:
 shutil.copy2(ROOT/'evidence'/name,evidence/name)
shutil.copy2(ROOT/'evidence/release-0.2.1/native-final-race.log',evidence/'native-final-race.log')

manifest={"version":"0.2.1","windowsRuntimeVerified":False,"files":{}}
for name in files:
 p=OUT/name
 obj=subprocess.check_output(['x86_64-w64-mingw32-objdump','-p',str(p)],text=True)
 dlls=[line.split(':',1)[1].strip() for line in obj.splitlines() if 'DLL Name:' in line]
 header=subprocess.check_output(['x86_64-w64-mingw32-objdump','-f',str(p)],text=True)
 if 'pei-x86-64' not in header:raise SystemExit('Not Windows x64: '+name)
 manifest['files'][name]={'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'dllImports':dlls,'format':'PE x86-64'}
buildinfo=subprocess.check_output(['go','version','-m',str(OUT/'forterm-native.exe')],text=True)
(OUT/'BUILD-INFO.txt').write_text(buildinfo)
for forbidden in ['chromium','webview','electron','nodejs']:
 if forbidden in buildinfo.lower():raise SystemExit('Unexpected dependency: '+forbidden)
deps=subprocess.check_output(['go','list','-deps','./cmd/forterm'],cwd=ROOT,text=True)
if 'forterm/native/internal/testserver' in deps:raise SystemExit('Test fixture in core')
(OUT/'COMPILED-PACKAGES.txt').write_text(deps)
(OUT/'VERIFICATION.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
with zipfile.ZipFile(ARCHIVE,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
 for path in sorted(OUT.rglob('*')):
  if path.is_file():z.write(path,NAME+'/'+str(path.relative_to(OUT)))
with zipfile.ZipFile(ARCHIVE) as z:
 bad=z.testzip()
 if bad:raise SystemExit('ZIP CRC failed: '+bad)
sha=hashlib.sha256(ARCHIVE.read_bytes()).hexdigest()
(RELEASE/'SHA256SUMS-Native-0.2.1.txt').write_text(sha+'  '+ARCHIVE.name+'\n')
manifest['archive']={'name':ARCHIVE.name,'bytes':ARCHIVE.stat().st_size,'sha256':sha,'zipCRC':'passed'}
(RELEASE/'VERIFICATION-Native-0.2.1.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(manifest,ensure_ascii=False,indent=2))

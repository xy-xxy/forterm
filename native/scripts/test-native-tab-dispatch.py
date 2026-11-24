#!/usr/bin/env python3
"""Run real Fyne processKeyPressed with an inert TestMain, never a native window."""
import pathlib,subprocess,json,hashlib,shutil,os
root=pathlib.Path(__file__).resolve().parents[1]
module=pathlib.Path(subprocess.check_output(['go','list','-m','-f','{{.Dir}}','fyne.io/fyne/v2'],cwd=root,text=True).strip())
evidence=root/'evidence/input-dev9/native-dispatch';evidence.mkdir(parents=True,exist_ok=True)
fork=evidence/'fyne-source'
if not fork.exists():shutil.copytree(module,fork)
workspace=evidence/'go.work';workspace.write_text('go 1.26.0\nuse (\n '+str(root)+'\n '+str(fork)+'\n)\n')
original=fork/'internal/driver/glfw/window_test.go';source=original.read_text();start=source.index('func TestMain(');brace=source.index('{',start);depth=1;end=brace+1
while depth:
 if source[end]=='{':depth+=1
 if source[end]=='}':depth-=1
 end+=1
assert 'createWindow("Master")' in source[start:end]
patched=source[:brace]+'{ os.Exit(m.Run()) }'+source[end:]
replacement=evidence/'window-no-native-main_test.go';replacement.write_text(patched)
injected=evidence/'forterm_tab_dispatch_test.go';injected.write_text((root/'scripts/driver-probe/dispatch_test.go.txt').read_text())
overlay=evidence/'overlay.json';overlay.write_text(json.dumps({'Replace':{str(original):str(replacement),str(fork/'internal/driver/glfw/forterm_tab_dispatch_test.go'):str(injected)}},indent=2))
(evidence/'scope.json').write_text(json.dumps({'upstreamWindowTestSHA256':hashlib.sha256(original.read_bytes()).hexdigest(),'realFunction':'window.processKeyPressed / capturesTab','TestMain':'replaced only in Go build overlay; no d.init/Run/createWindow/Master','moduleCacheModified':False,'nativeWindowCreated':False},indent=2))
with (evidence/'result.log').open('w') as log:
 result=subprocess.run(['go','test','-mod=readonly','-overlay',str(overlay),'fyne.io/fyne/v2/internal/driver/glfw','-run','^TestForterm(ActualTabDispatcher|DispatcherReachesSyntheticPTYAndSSH)$','-count=1','-v'],cwd=root,stdout=log,stderr=subprocess.STDOUT,env={**os.environ,'GOWORK':str(workspace)})
print((evidence/'result.log').read_text()[-4000:]);raise SystemExit(result.returncode)

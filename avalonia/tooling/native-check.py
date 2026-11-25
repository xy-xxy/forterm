#!/usr/bin/env python3
"""Starts the packaged Studio with a real window through its xshell/xftp entries, against a loopback fixture, and checks
what arrives. Own data directory; no credential store is read. Windows and macOS.

  native-check.py --app FOLDER --out FOLDER      (run it with the fixture environment's python)"""
import argparse, hashlib, json, os, pathlib, subprocess, sys, tempfile, threading, time, shutil
root = pathlib.Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(); p.add_argument('--app', required=True); p.add_argument('--out', required=True); p.add_argument('--idle', action='store_true')
a = p.parse_args()
app, out = pathlib.Path(a.app).resolve(), pathlib.Path(a.out).resolve(); out.mkdir(parents=True, exist_ok=True)
windows = os.name == 'nt'
exe = lambda n: str(app / (n + '.exe' if windows else n))
data = pathlib.Path(tempfile.mkdtemp(prefix='ft-native-'))
started = []
def end_all():
    # Only what was started here: somebody may be using the program.
    for q in started:
        if windows: subprocess.run(['taskkill', '/F', '/T', '/PID', str(q.pid)], capture_output=True)
        elif q.poll() is None:
            for child in subprocess.run(['pgrep', '-P', str(q.pid)], capture_output=True, text=True).stdout.split(): subprocess.run(['kill', '-TERM', child])
def too_long(): print('FAILED: took too long', flush=True); end_all(); os._exit(3)
watchdog = threading.Timer(420, too_long); watchdog.daemon = True; watchdog.start()
env = dict(os.environ, FORTERM_STUDIO_DATA=str(data))
class Fixture:
    def __init__(self):
        self.process = subprocess.Popen([sys.executable, str(root / 'tooling/ssh_fixture.py')], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, env=dict(os.environ, FIXTURE_POSIX='1'))
        info = json.loads(self.process.stdout.readline())
        self.key = hashlib.sha256(f"127.0.0.1:{info['port']}".encode()).hexdigest().upper(); self.fingerprint = info['fingerprint']
        self.url = f"ssh://synthetic:synthetic-only@127.0.0.1:{info['port']}"
    def ask(self, line): self.process.stdin.write(line + '\n'); self.process.stdin.flush(); return json.loads(self.process.stdout.readline())
    def typed(self): return bytes.fromhex(''.join(self.ask('stats')['input']))
    def stop(self):
        try: self.process.stdin.write('stop\n'); self.process.stdin.flush(); self.process.wait(5)
        except Exception: self.process.kill()
# The first for the single launches; one each for those that come at the same moment (the same one would be one session).
first_server, *others = servers = [Fixture() for _ in range(6)]
ask, typed, url = first_server.ask, first_server.typed, first_server.url
(data / 'known-hosts.json').write_text(json.dumps({f.key: f.fingerprint for f in servers}))
(data / 'preferences.json').write_text(json.dumps({'IdleKeepAlive': a.idle, 'IdleMinutes': 1}))
def programs():
    if windows: return [l for l in subprocess.run(['tasklist', '/FI', 'IMAGENAME eq Forterm.exe', '/FO', 'CSV', '/NH'], capture_output=True, text=True).stdout.splitlines() if 'Forterm.exe' in l]
    return subprocess.run(['pgrep', '-f', exe('Forterm')], capture_output=True, text=True).stdout.split()
if programs():
    print('Forterm is running here; not started.', programs())
    for f in servers: f.stop()
    sys.exit(2)
results, failed = [], []
def check(what, ok, detail=''):
    results.append({'check': what, 'ok': bool(ok), 'detail': str(detail)}); print('ok ' if ok else 'FAILED', what, detail, flush=True)
    if not ok: failed.append(what)
def until(condition, seconds):
    end = time.time() + seconds
    while time.time() < end:
        if condition(): return True
        time.sleep(.5)
    return False
def shot(name):
    if not windows: return
    script = "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; $b=[System.Windows.Forms.SystemInformation]::VirtualScreen; $i=New-Object System.Drawing.Bitmap $b.Width,$b.Height; ([System.Drawing.Graphics]::FromImage($i)).CopyFromScreen($b.Left,$b.Top,0,0,$i.Size); $i.Save('" + str(out / (name + '.png')) + "')"
    subprocess.run(['powershell', '-NoProfile', '-Command', script], capture_output=True, timeout=60)
# Windows only: the real mouse and keyboard, and where things are on the screen (UI Automation).
UI = r'''
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Native {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr w);
}
"@
[void][Native]::SetProcessDPIAware()
$A=[System.Windows.Automation.AutomationElement]; $T=[System.Windows.Automation.TreeScope]
function By($p,$v) { New-Object System.Windows.Automation.PropertyCondition($p,$v) }
$process=Get-Process Forterm | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$C=[System.Windows.Automation.ControlType]
# A window that belongs to another is below it.
function Windows { $main.FindAll($T::Descendants,(By $A::ControlTypeProperty $C::Window)) }
$main=$A::FromHandle($process.MainWindowHandle)
function Find($in,$name,$type=$C::Button) { $in.FindFirst($T::Descendants,(New-Object System.Windows.Automation.AndCondition((By $A::NameProperty $name),(By $A::ControlTypeProperty $type)))) }
function Click($x,$y,$times=1) { [void][Native]::SetCursorPos([int]$x,[int]$y); Start-Sleep -Milliseconds 150; for($i=0;$i -lt $times;$i++){ [Native]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [Native]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60 }; Start-Sleep -Milliseconds 900 }
function ClickOn($e) { $r=$e.Current.BoundingRectangle; Click ($r.X+$r.Width/2) ($r.Y+$r.Height/2) }
[void][Native]::SetForegroundWindow($process.MainWindowHandle); Start-Sleep -Milliseconds 500
'''
def ui(script):
    import base64
    code = base64.b64encode((UI + script).encode('utf-16-le')).decode()
    done = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', code], capture_output=True, timeout=90)
    return done.stdout.decode('utf-8', 'replace').strip() or 'ERROR ' + done.stderr.decode('utf-8', 'replace')[-400:]
first = None
try:
    first = subprocess.Popen([exe('xshell'), '-newtab', '合成演示', '-url', url], env=env); started.append(first)
    check('xshell opens one shell', until(lambda: ask('stats')['shells'] == 1, 60))
    check('and the files on the same connection', until(lambda: ask('stats')['sftp'] == 1, 30))
    time.sleep(6); shot('terminal')
    s = ask('stats'); check('one connection, one login, one program', s['connections'] == 1 and s['auth'] == 1 and len(programs()) == 1, f"connections {s['connections']} logins {s['auth']} programs {len(programs())}")
    check('nothing was typed into the terminal', typed() == b'', typed())
    check('the entry waits for the program', first.poll() is None, first.poll())
    two = [subprocess.Popen([exe('xftp'), '-newtab', '合成文件', '-url', url], env=env) for _ in range(2)]; started.extend(two)
    check('later entries hand over and end with 0', until(lambda: all(q.poll() is not None for q in two), 30) and all(q.returncode == 0 for q in two), [q.returncode for q in two])
    time.sleep(5); shot('files')
    s = ask('stats'); check('the same launch twice is one session', s['sftp'] == 2 and s['shells'] == 1 and s['connections'] == 2 and s['auth'] == 2 and len(programs()) == 1, f"sftp {s['sftp']} shells {s['shells']} connections {s['connections']} logins {s['auth']}")
    many = [subprocess.Popen([exe('xshell'), '-newtab', f'批量 {i + 1}', '-url', f.url], env=env) for i, f in enumerate(others)]; started.extend(many)
    check('five different ones at once are all taken', until(lambda: all(q.poll() is not None for q in many), 40) and all(q.returncode == 0 for q in many), [q.returncode for q in many])
    check('and open five sessions', until(lambda: all(f.ask('stats')['shells'] == 1 for f in others), 60), [f.ask('stats')['shells'] for f in others]); time.sleep(3); shot('many')
    if a.idle:
        check('keep-alive sends into each of the six after a minute', until(lambda: all(f.typed() == b' \x7f' for f in servers), 110), [f.typed() for f in servers])
    else:
        time.sleep(65); check('off: still nothing after a minute', all(f.typed() == b'' for f in servers), [f.typed() for f in servers])
    if windows:
        answer = ui("$w=$main.Current.BoundingRectangle; $s=[System.Windows.Forms.SystemInformation]::WorkingArea; \"$([int]$w.X) $([int]$w.Y) $([int]$w.Right) $([int]$w.Bottom) | $($s.X) $($s.Y) $($s.Right) $($s.Bottom)\"")
        try: w, area = [[int(v) for v in part.split()] for part in answer.split('|')]; inside = w[0] >= area[0] - 9 and w[1] >= area[1] - 9 and w[2] <= area[2] + 9 and w[3] <= area[3] + 9
        except Exception: inside = False
        check('the whole window is on the screen', inside, answer)
        before = [len(f.typed()) for f in servers]
        answer = ui("ClickOn (Find $main '系统配置'); (Windows | ForEach-Object { $_.Current.Name }) -join '|'"); shot('settings')
        check('the mouse opens the settings', '系统配置' in answer.split('|'), answer)
        answer = ui("foreach($x in Windows){ $x.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }; Start-Sleep 1; @(Windows).Count"); check('and close them again', answer == '0', answer)
        answer = ui("$r=$main.Current.BoundingRectangle; $t=Find $main '系统配置'; Click ($t.Current.BoundingRectangle.X-120) ($r.Y+18) 2; $z=[Native]::IsZoomed($process.MainWindowHandle); Click ($t.Current.BoundingRectangle.X-120) ($r.Y+18) 1; \"$z\""); shot('maximised')
        check('a double click on the title bar maximises', answer == 'True', answer)
        # Into the terminal in front: text, Chinese, Enter.
        answer = ui("$r=$main.Current.BoundingRectangle; Click ($r.X+$r.Width*0.5) ($r.Y+$r.Height*0.5); [System.Windows.Forms.SendKeys]::SendWait('echo ok{ENTER}'); Start-Sleep -Milliseconds 800; 'sent'")
        arrived = until(lambda: any(f.typed()[n:] == b'echo ok\r' for f, n in zip(servers, before)), 8)
        check('the keyboard reaches the terminal, and only that one', arrived and sum(len(f.typed()) - n for f, n in zip(servers, before)) == 8, [f.typed()[n:] for f, n in zip(servers, before)]); shot('typed')
    check('still running', first.poll() is None and len(programs()) == 1)
finally:
    end_all()
    if first is not None:
        try: first.wait(20)
        except subprocess.TimeoutExpired: first.kill()
        check('the entry ends with the program', first.returncode is not None, first.returncode)
    for f in servers: f.stop()
    (out / 'native-check.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    shutil.rmtree(data, ignore_errors=True)
sys.exit(1 if failed else 0)

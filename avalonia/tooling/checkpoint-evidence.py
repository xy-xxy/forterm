#!/usr/bin/env python3
"""Supplement a newly frozen checkpoint. Refuses source drift; runs no-window diagnostics only."""
import os, pathlib, json, hashlib, struct, zipfile, subprocess, sys
root = pathlib.Path(__file__).resolve().parents[1]
repo = root.parent
release = repo / 'release'
version = sys.argv[1]
tag = 'alpha' + version.rsplit('.', 1)[1]
identity = json.loads((release / ('IDENTITY-Avalonia-' + version + '.json')).read_text())
checkpoint = pathlib.Path(identity['checkpoint'])
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
manifest = json.loads((checkpoint / 'SOURCE-HASHES.json').read_text())
for name, expected in manifest.items():
    assert sha(root / name) == expected, name
mac = pathlib.Path(identity['macApp']) / 'Contents/MacOS'
win = root / ('artifacts/' + tag + '-win-x64')
env = dict(os.environ, DOTNET_ROOT='/tmp/forterm-no-runtime', DOTNET_MULTILEVEL_LOOKUP='0')
runtime = subprocess.check_output([str(mac / 'Forterm'), '--runtime-check'], env=env, text=True)
diagnostic = subprocess.check_output([str(mac / 'xftp'), '--diagnose', '-newtab', 'SYNTHETIC', '-url', 'sftp://synthetic:synthetic-only@127.0.0.1:1'], text=True)
parsed = json.loads(runtime)
assert parsed['Version'] == version and parsed['VisibleWindowsCreated'] == 0
assert not parsed['CredentialApi']['CredentialsAccessed']
assert parsed['TerminalEnginePatch']=='search-newest-v16' and parsed['TerminalControlPatch']=='review-highlight-v17'
assert not json.loads(diagnostic)['networkRequested']
(root / ('evidence/' + tag + '-packaged-runtime.json')).write_text(runtime)
(root / ('evidence/' + tag + '-packaged-xftp-diagnostic.json')).write_text(diagnostic)
names = ['Forterm.exe', 'xshell.exe', 'xftp.exe', 'forterm-diagnose.exe', 'Forterm.dll', 'Forterm.Core.dll', 'Renci.SshNet.dll', 'AvaloniaEdit.dll', 'XTerm.NET.dll', 'Iciclecreek.Avalonia.Terminal.dll', 'Forterm.Launcher.dll', 'Forterm.Diagnose.dll']
if (win/'FluentFTP.dll').exists(): names.append('FluentFTP.dll')
payload = {'version': version, 'windows': {n: sha(win/n) for n in names},
           'mac': {n: sha(mac/n) for n in (['Forterm', 'Forterm.dll', 'Forterm.Core.dll', 'Renci.SshNet.dll', 'AvaloniaEdit.dll', 'XTerm.NET.dll', 'Iciclecreek.Avalonia.Terminal.dll'] + (['FluentFTP.dll'] if (mac/'FluentFTP.dll').exists() else []))}, 'runtimeCheck': parsed}
(release / ('PAYLOADS-Avalonia-' + version + '.json')).write_text(json.dumps(payload, indent=2) + '\n')
with zipfile.ZipFile(identity['windowsPrototypeZIP']) as archive:
    assert archive.testzip() is None
    largest = sorted([{'path': p.filename, 'bytes': p.file_size} for p in archive.infolist()], key=lambda p: p['bytes'], reverse=True)[:12]
size = {'version': version, 'macBytes': identity['normalMacBytes'], 'windowsZIPBytes': identity['normalWindowsZIPBytes'],
        'windowsExpandedBytes': identity['normalWindowsExpandedBytes'], 'symbolsZIPBytes': pathlib.Path(identity['symbolsArchive']).stat().st_size,
        'pdbInNormalPackage': False, 'largestWindowsFiles': largest}
(release / ('SIZE-Avalonia-' + version + '.json')).write_text(json.dumps(size, indent=2) + '\n')
folder = root / ('evidence/ui-' + version)
screens = {}
for path in sorted(folder.glob('*.png')):
    w, h = struct.unpack('>II', path.read_bytes()[16:24])
    screens[path.name] = {'width': w, 'height': h, 'sha256': sha(path)}
(folder / 'SCREENSHOTS.json').write_text(json.dumps({'version': version, 'headlessSoftwareNotNativeGui': True, 'syntheticLoopbackOnly': True, 'images': screens}, indent=2) + '\n')
audit = root / 'evidence/native-imports.json'
assert json.loads(audit.read_text())['version'] == version
(root / ('evidence/' + tag + '-native-imports.json')).write_bytes(audit.read_bytes())
print(json.dumps({'version': version, 'sourceMatches': len(manifest), 'screenshots': len(screens), 'runtimeVisibleWindows': parsed['VisibleWindowsCreated'], 'credentialsAccessed': False, 'windowsZIPBytes': size['windowsZIPBytes']}, indent=2))

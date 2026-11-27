#!/usr/bin/env python3
"""Builds the Windows installer of Studio from files that are already published.

  package-studio-windows.py 0.4.0-alpha.13                 from ../release/Forterm-Studio-<ver>-windows-x64.zip
  package-studio-windows.py 0.4.0-alpha.13 --payload DIR --output FILE [--makensis FILE]   from a folder (CI)

Runs no Windows program. Never replaces an existing installer."""
import argparse, hashlib, json, os, pathlib, shutil, subprocess, sys, tempfile, zipfile
root = pathlib.Path(__file__).resolve().parents[1]
release = root.parent / 'release'
p = argparse.ArgumentParser()
p.add_argument('version'); p.add_argument('--payload'); p.add_argument('--output'); p.add_argument('--makensis')
a = p.parse_args()
sha = lambda f: hashlib.sha256(pathlib.Path(f).read_bytes()).hexdigest()
output = pathlib.Path(a.output).resolve() if a.output else release / f'Forterm-Studio-{a.version}-windows-x64-setup.exe'
if output.exists(): raise SystemExit(f'Refusing to overwrite {output}')
compiler = pathlib.Path(a.makensis or os.environ.get('FORTERM_MAKENSIS') or pathlib.Path.home() / '.cache/forterm-build-tools/nsis-3.04-native-arm64/mac/makensis')
env = dict(os.environ)
if not a.makensis and 'FORTERM_MAKENSIS' not in os.environ:
    recorded = json.loads((root / 'tooling/nsis-native-arm64-toolchain.json').read_text())
    assert sha(compiler) == recorded['compilerSHA256'], 'makensis is not the recorded one'
    env['NSISDIR'] = str(compiler.parents[1])
work = pathlib.Path(tempfile.mkdtemp(prefix='forterm-studio-setup-'))
try:
    if a.payload: payload = pathlib.Path(a.payload).resolve()
    else:
        archive = release / f'Forterm-Studio-{a.version}-windows-x64.zip'
        assert (release / (archive.name + '.sha256')).read_text().split()[0] == sha(archive), 'the ZIP is not the published one'
        with zipfile.ZipFile(archive) as z:
            assert z.testzip() is None
            assert all(not n.startswith('/') and '..' not in pathlib.PurePosixPath(n).parts for n in z.namelist())
            z.extractall(work / 'zip')
        payload = work / 'zip' / f'Forterm-Studio-{a.version}'
    files = sorted(f.relative_to(payload).as_posix() for f in payload.rglob('*') if f.is_file())
    for needed in ['Forterm.exe', 'xshell.exe', 'xftp.exe', 'forterm-diagnose.exe', 'README.txt']:
        assert needed in files, needed
    assert not any(f.lower().endswith('.pdb') for f in files)
    # Exactly what is installed is removed, and folders only when nothing else is left in them.
    escape = lambda s: s.replace('/', '\\').replace('$', '$$').replace('"', '$\\"')
    folders = {str(q) for f in files for q in pathlib.PurePosixPath(f).parents if str(q) != '.'}
    still = lambda f, name: f'${{If}} ${{FileExists}} "$INSTDIR\\{escape(f)}"\n  StrCpy $3 "{escape(name)}"\n${{EndIf}}'
    lines = [f'Delete "$INSTDIR\\{escape(f)}"' for f in files] + [still(f, f) for f in files] + [f'RMDir "$INSTDIR\\{escape(d)}"' for d in sorted(folders, key=lambda d: (-d.count('/'), d))]
    listing = work / 'uninstall-files.nsh'
    listing.write_text('\n'.join(lines) + '\n', encoding='utf-8-sig')
    taken = work / 'taken-files.nsh'
    taken.write_text('\n'.join(still(f, f) for f in [*files, 'Uninstall-Forterm-Studio.exe']) + '\n', encoding='utf-8-sig')
    numeric = a.version.split('-')[0] + '.' + a.version.rsplit('.', 1)[1]
    kilobytes = sum((payload / f).stat().st_size for f in files) // 1024
    built = work / output.name
    command = [str(compiler), '-V2', '-INPUTCHARSET', 'UTF8', f'-DAPP_VERSION={a.version}', f'-DAPP_NUMERIC={numeric}', f'-DSOURCE_DIR={payload}', f'-DOUTPUT_FILE={built}',
               f'-DUNINSTALL_FILES={listing}', f'-DTAKEN_FILES={taken}', f'-DICON_FILE={root / "src/Forterm.Studio/Assets/forterm.ico"}', f'-DESTIMATED_KB={kilobytes}', str(root / 'packaging/studio-installer.nsi')]
    done = subprocess.run(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace')
    if done.returncode != 0 or not built.exists(): print(done.stdout); raise SystemExit('makensis failed')
    if 'warning' in done.stdout.lower(): print(done.stdout); raise SystemExit('makensis warned')
    output.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(built), str(output)) # only when all is well: a failed try leaves nothing in the way of the next
    if not a.output: (release / (output.name + '.sha256')).write_text(f'{sha(output)}  ../release/{output.name}\n')
    print(json.dumps({'output': str(output), 'sha256': sha(output), 'bytes': output.stat().st_size, 'files': len(files), 'kilobytes': kilobytes}, ensure_ascii=False))
finally: shutil.rmtree(work, ignore_errors=True)

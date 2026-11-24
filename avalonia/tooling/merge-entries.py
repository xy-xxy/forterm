#!/usr/bin/env python3
import pathlib,shutil,sys,os
root=pathlib.Path(__file__).resolve().parents[1]
for rid in sys.argv[1:]:
 tag=os.environ.get('FORTERM_BUILD_TAG','alpha33')
 target=root/('artifacts/'+tag+'-'+rid)
 launcher=root/('artifacts/'+tag+'-launcher-'+rid);diagnose=root/('artifacts/'+tag+'-diagnose-'+rid)
 ext='.exe' if rid.startswith('win') else ''
 for folder in [launcher,diagnose]:
  for path in folder.rglob('*'):
   if path.is_file() and path.name.startswith(("Forterm.Launcher","Forterm.Diagnose")):
    rel=path.relative_to(folder)
    # Dependencies are already shared by the same pinned SDK. Additional
    # launcher assets stay app-local; never start another runtime process host.
    dest=target/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,dest)
 for name in ['xshell','xftp']:shutil.copy2(target/('Forterm.Launcher'+ext),target/(name+ext))
 shutil.copy2(target/('Forterm.Diagnose'+ext),target/('forterm-diagnose'+ext))
 for name in ['Forterm.Launcher'+ext,'Forterm.Diagnose'+ext]:(target/name).unlink()
 print(rid,'four named entries ready')

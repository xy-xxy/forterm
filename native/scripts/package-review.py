#!/usr/bin/env python3
"""Freeze a development review ZIP, without overwriting any earlier artifact."""
import argparse, hashlib, json, pathlib, shutil, subprocess, tarfile, zipfile
root=pathlib.Path(__file__).resolve().parents[1]
release=root.parent/'release'
parser=argparse.ArgumentParser();parser.add_argument('--version',required=True);parser.add_argument('--notes',required=True);parser.add_argument('--tests',default='See REVIEW-NOTES.md');args=parser.parse_args();version=args.version
name='Forterm-Native-'+version+'-review-windows-x64'
out=release/name
checkpoint=root.parent/('checkpoints/native-'+version+'-review-20261028')
archive=release/(name+'.zip')
if out.exists() or checkpoint.exists() or archive.exists():
 raise SystemExit('Refusing to overwrite review candidate')
out.mkdir();checkpoint.mkdir()
files={}
for name_ in ['forterm-native.exe','xshell.exe','xftp.exe','forterm-diagnose.exe']:
 path=root/('evidence/release-'+version+'/windows')/name_
 header=subprocess.check_output(['x86_64-w64-mingw32-objdump','-f',str(path)],text=True)
 if 'pei-x86-64' not in header:raise SystemExit('Not AMD64: '+name_)
 shutil.copy2(path,out/name_)
 files[name_]={'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size,'machine':'AMD64'}
shutil.copy2(root/args.notes,out/'REVIEW-NOTES.md')
shutil.copytree(release/'Forterm-Native-0.2.1-windows-x64/licenses',out/'licenses')
sources={}
paths=[]
for path in root.rglob('*'):
 rel=path.relative_to(root)
 if not path.is_file() or any(p in {'evidence','__pycache__','.git'} for p in rel.parts) or rel.as_posix()=='cmd/portable/payload.zip':continue
 paths.append(path)
 if path.suffix in {'.go','.c','.h','.m','.mod','.sum'}:sources[str(rel)]=hashlib.sha256(path.read_bytes()).hexdigest()
manifest=out/'SOURCE-HASHES.json'
manifest.write_text(json.dumps(sources,indent=2)+'\n')
with tarfile.open(checkpoint/'native-source.tar.gz','w:gz') as tar:
 for path in paths:tar.add(path,arcname='native/'+str(path.relative_to(root)),recursive=False)
identity={'version':version,'reviewOnly':True,'windowsRuntimeVerifiedForThisBuild':False,'files':files,'offlineReviewCommand':'forterm-diagnose.exe --ui-check','normalStartupReadsUserConfiguration':True,'sourceManifestSHA256':hashlib.sha256(manifest.read_bytes()).hexdigest(),'sourceArchiveSHA256':hashlib.sha256((checkpoint/'native-source.tar.gz').read_bytes()).hexdigest(),'sourceCheckpoint':str(checkpoint.relative_to(root.parent))+'/native-source.tar.gz','tests':args.tests }
(out/'BUILD-ID.json').write_text(json.dumps(identity,ensure_ascii=False,indent=2)+'\n')
for file in ['SOURCE-HASHES.json','BUILD-ID.json']:shutil.copy2(out/file,checkpoint/file)
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
 for path in sorted(out.rglob('*')):
  if path.is_file():z.write(path,name+'/'+str(path.relative_to(out)))
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 for file,info in files.items():assert hashlib.sha256(z.read(name+'/'+file)).hexdigest()==info['sha256']
sha=hashlib.sha256(archive.read_bytes()).hexdigest()
sums=release/('SHA256SUMS-Native-'+version+'-review.txt')
sums.write_text(sha+'  '+archive.name+'\n'+''.join(v['sha256']+'  '+k+'\n' for k,v in files.items()))
shutil.copy2(sums,checkpoint/sums.name)
print(json.dumps({'archive':str(archive),'sha256':sha,'mainPE':files['forterm-native.exe']['sha256'],'MiB':round(archive.stat().st_size/1048576,2)},indent=2))

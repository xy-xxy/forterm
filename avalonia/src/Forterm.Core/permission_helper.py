import os,sys,json,stat
p=json.loads(sys.argv[1])
if not hasattr(os,'O_NOFOLLOW') or os.open not in os.supports_dir_fd: raise RuntimeError('safe no-follow API unavailable')
name=p['path']
if not name.startswith('/') or name=='/' or '\x00' in name or '\n' in name or '\r' in name: raise RuntimeError('unsafe path')
parts=name[1:].split('/')
if any(v in ('','.', '..') for v in parts): raise RuntimeError('unsafe component')
fd=os.open('/',os.O_RDONLY|os.O_DIRECTORY)
try:
 for v in parts[:-1]:
  nxt=os.open(v,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW,dir_fd=fd);os.close(fd);fd=nxt
 flags=os.O_NOFOLLOW|os.O_NONBLOCK
 if p['action']=='inspect': flags|=getattr(os,'O_PATH',os.O_RDONLY)
 else: flags|=os.O_RDONLY
 target=os.open(parts[-1],flags,dir_fd=fd)
 try:
  s=os.fstat(target)
  if not stat.S_ISDIR(s.st_mode) and not stat.S_ISREG(s.st_mode): raise RuntimeError('unsupported file type')
  current={'device':s.st_dev,'inode':s.st_ino,'size':s.st_size,'mode':s.st_mode&4095,'modifiedNS':s.st_mtime_ns,'directory':stat.S_ISDIR(s.st_mode)}
  if p['action']=='apply':
   if os.geteuid()!=0: raise RuntimeError('sudo root identity not obtained')
   if current!=p['expected']: raise RuntimeError('confirmed target changed')
   mode=p['mode']
   if not isinstance(mode,int) or mode<0 or mode>4095: raise RuntimeError('unsafe permission mode')
   os.fchmod(target,mode)
   if (os.fstat(target).st_mode&4095)!=mode: raise RuntimeError('permission verification failed')
  elif p['action']!='inspect': raise RuntimeError('unsupported action')
  print(json.dumps(current,sort_keys=True))
 finally: os.close(target)
finally: os.close(fd)

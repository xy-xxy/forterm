"""Owned loopback-only FTP protocol fixture. No production credentials or network."""
import os,sys,json,tempfile,threading,hashlib,logging
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler
from pyftpdlib.servers import ThreadedFTPServer
logging.getLogger('pyftpdlib').setLevel(logging.CRITICAL)
root=tempfile.TemporaryDirectory(prefix='forterm-csharp-ftp-')
for name in ['a','b']:os.mkdir(os.path.join(root.name,name))
open(os.path.join(root.name,'edit.txt'),'wb').write('合成FTP文件\r\nline\n'.encode('utf-8'))
os.symlink(os.path.join(root.name,'edit.txt'),os.path.join(root.name,'link.txt'))
os.symlink(os.path.join(root.name,'a'),os.path.join(root.name,'link-dir'))
lock=threading.Lock()
stats={'connections':0,'auth':0,'commands':[]}
drop=''
class Handler(FTPHandler):
 def on_connect(self):
  with lock:stats['connections']+=1
 def on_login(self,user):
  with lock:stats['auth']+=1
 def pre_process_command(self,line,cmd,arg):
  with lock:stats['commands'].append({'command':cmd,'argument':'<redacted>' if cmd in ['USER','PASS'] else arg})
  self.last_command=cmd
  if cmd in ['LIST','STOR','RETR']:self.data_command=cmd
  return super().pre_process_command(line,cmd,arg)
 def respond(self,resp,*args,**kwargs):
  if resp.startswith('226') and getattr(self,'data_command','')==drop:return
  if resp.startswith('250') and getattr(self,'last_command','')==drop:return
  return super().respond(resp,*args,**kwargs)
authorizer=DummyAuthorizer();authorizer.add_user('synthetic','synthetic-only',root.name,perm='elradfmwMT')
Handler.authorizer=authorizer;Handler.banner='Forterm synthetic FTP fixture';Handler.passive_ports=None
server=ThreadedFTPServer(('127.0.0.1',0),Handler)
thread=threading.Thread(target=server.serve_forever,kwargs={'timeout':.05,'blocking':True,'handle_exit':False},daemon=True);thread.start()
print(json.dumps({'port':server.socket.getsockname()[1]}),flush=True)
for line in sys.stdin:
 command=line.strip()
 if command=='stats':
  with lock:report=json.dumps(stats)
  print(report,flush=True)
 elif command=='large':
  with open(os.path.join(root.name,'a/large.bin'),'wb') as output:
   block=bytes(range(256))*128
   for _ in range(544):output.write(block)
   output.write(b'large-copy-end')
  print(json.dumps({'largeReady':True}),flush=True)
 elif command=='files':
  files={}
  for directory,dirs,names in os.walk(root.name):
   for name in names:
    path=os.path.join(directory,name)
    if not os.path.islink(path):
     with open(path,'rb') as f:files[os.path.relpath(path,root.name)]=hashlib.sha256(f.read()).hexdigest()
  print(json.dumps(files),flush=True)
 elif command.startswith('drop-reply '):
  drop=command.split(' ',1)[1];print(json.dumps({'drop':drop}),flush=True)
 elif command=='disconnect':
  server.close_all();print(json.dumps({'disconnected':True}),flush=True)
 elif command=='stop':break
server.close_all()

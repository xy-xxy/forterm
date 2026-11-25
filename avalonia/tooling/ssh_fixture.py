"""Owned synthetic loopback SSH/SFTP fixture. Never accepts external addresses."""
import os,socket,threading,json,sys,tempfile,paramiko,time,struct
root=tempfile.TemporaryDirectory(prefix='forterm-csharp-fixture-')
# evidence/ is not part of the repository: a fresh checkout has none.
os.makedirs(os.path.join(os.path.dirname(__file__),'../evidence'),exist_ok=True)
paramiko.util.log_to_file(os.path.join(os.path.dirname(__file__),'../evidence/fixture-paramiko.log'))
key=paramiko.RSAKey.generate(2048)
algorithm=os.getenv('FIXTURE_USERKEY_ALGO','rsa')
if algorithm=='ecdsa':userkey=paramiko.ECDSAKey.generate(bits=256)
elif algorithm=='ed25519':
 from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
 from cryptography.hazmat.primitives import serialization
 import io
 encoded=Ed25519PrivateKey.generate().private_bytes(serialization.Encoding.PEM,serialization.PrivateFormat.OpenSSH,serialization.NoEncryption())
 userkey=paramiko.Ed25519Key.from_private_key(io.StringIO(encoded.decode()))
else:userkey=paramiko.RSAKey.generate(2048)
keypath=os.path.join(root.name,'synthetic-user-key.pem')
os.mkdir(os.path.join(root.name,'a'));os.mkdir(os.path.join(root.name,'b'))
if algorithm=='ed25519':open(keypath,'wb').write(encoded)
else:userkey.write_private_key_file(keypath,password='synthetic-phrase' if os.getenv('FIXTURE_KEY_ENCRYPTED')=='1' else None)
counts={'connections':0,'auth':0,'passwordRequests':0,'keyChecks':0,'openModes':[],'shells':0,'sftp':0,'forwarded':0,'globals':0,'lstats':0,'resizes':[],'resizesDetailed':[],'ptyRequests':[],'frameMarkers':{},'input':[],'sudoPrompts':0,'sudoPasswordBytes':0,'execs':0,'commands':[],'lists':[],'env':[],'negotiated':[],'opens':[],'ptyModes':[]}
lock=threading.Lock()
transports=[]
shell_channels=[]
shell_sizes={}
resize_tui=os.getenv("FIXTURE_RESIZE_TUI")=="1"
def draw_resize_frame(ch):
 import uuid
 with lock:
  w,h=shell_sizes.get(ch.get_id(),(80,24));token=counts["frameMarkers"].setdefault(str(ch.get_id()),uuid.uuid4().hex)
 try:ch.sendall(("\x1b[H\x1b[2JFRAME %dx%d"%(w,h)+("\x1b[2;1H"+token[:max(0,w-1)] if h>=2 else "")).encode())
 except Exception:pass
swap_permission_target=False
wrong_sudo_prompt=False
_really_parse_kex_init=paramiko.Transport._really_parse_kex_init
def _recording_parse_kex_init(self,*args,**kwargs):
 # paramiko keeps no agreed kex name; as the server it takes the client's first offer it supports.
 parsed=_really_parse_kex_init(self,*args,**kwargs)
 if self.server_mode:self.fixture_kex=next((k for k in parsed['kex_algo_list'] if k in self.preferred_kex),'')
 return parsed
paramiko.Transport._really_parse_kex_init=_recording_parse_kex_init
# FIXTURE_ODD, what some servers do after the login: 'close-after-login', 'close-on-open', 'close-on-request' (the connection ends
# without a word), 'disconnect-on-open' (with one), 'refuse' (every session channel, with FIXTURE_TEXT), 'refuse-then-close' (the
# second time a channel is asked for, the connection ends). FIXTURE_BANNER: shown before the login is complete.
odd=os.getenv('FIXTURE_ODD','')
odd_text=os.getenv('FIXTURE_TEXT','')
def hang_up(transport):
 try:transport.sock.shutdown(socket.SHUT_RDWR)
 except OSError:pass
_parse_channel_open=paramiko.Transport._parse_channel_open
def _odd_channel_open(self,m):
 kind=m.get_text();chanid=m.get_int();window=m.get_int();packet=m.get_int();m.rewind()
 if not self.server_mode:return _parse_channel_open(self,m)
 with lock:counts['opens'].append({'kind':kind,'window':window,'packet':packet});asked=len(counts['opens'])
 if odd=='close-after-login':return
 if odd=='close-on-open' or odd=='refuse-then-close' and asked>1:return hang_up(self)
 if odd=='disconnect-on-open':
  bye=paramiko.Message();bye.add_byte(bytes([1]));bye.add_int(11);bye.add_string(odd_text);bye.add_string('en');self._send_message(bye);return hang_up(self)
 if odd in ('refuse','refuse-then-close') and kind=='session':
  no=paramiko.Message();no.add_byte(bytes([92]));no.add_int(chanid);no.add_int(2);no.add_string(odd_text);no.add_string('en');return self._send_message(no)
 return _parse_channel_open(self,m)
paramiko.Transport._parse_channel_open=_odd_channel_open
def snapshot():
 with lock:
  result=dict(counts);result['activeTransports']=sum(1 for t in transports if t.is_active());return result
def studio_monitor(n):
 """Synthetic Studio monitor probe output; counters grow with each call."""
 return ('FTS1\nOS\nNAME="Ubuntu"\nVERSION="24.04.5 LTS (Noble Numbat)"\nID=ubuntu\nPRETTY_NAME="Ubuntu 24.04.5 LTS"\n'
  'TZ\nCST +0800\nUPTIME\n%d.5 10.0\nLOAD\n0.10 0.20 0.30 1/100 1000\n'
  'CPU\ncpu %d 0 %d %d 0 0 0 0 0 0\ncpu0 %d 0 10 %d 0 0 0 0 0 0\ncpu1 %d 0 10 %d 0 0 0 0 0 0\n'
  'MEM\nMemTotal: 8000000 kB\nMemFree: 2000000 kB\nMemAvailable: 5000000 kB\nBuffers: 500000 kB\nCached: 2000000 kB\nSReclaimable: 500000 kB\nSwapTotal: 1000000 kB\nSwapFree: 1000000 kB\n'
  'DF\nFilesystem Type 1024-blocks Used Available Capacity Mounted on\n/dev/sda1 ext4 100000000 20000000 80000000 20%% /\n/dev/sdb1 xfs 50000000 1000000 49000000 2%% /data\n'
  'DISKSTATS\n   8       1 sda1 100 0 %d 0 200 0 %d 0 0 0 0\n   8      17 sdb1 10 0 %d 0 20 0 %d 0 0 0 0\n'
  'NET\nInter-|   Receive\n face |bytes\n    lo: 1000 10 0 0 0 0 0 0 1000 10 0 0 0 0 0 0\n  eth0: %d 100 0 0 0 0 0 0 %d 50 0 0 0 0 0 0\n'
  'PS\n 4242 synthetic Ss   12.5 102400 Sat Nov 22 15:39:59 2026 /usr/bin/java -jar app.jar --spring.profiles.active=prod\n'
  '    1 root     Ss    0.0  13000 Mon Nov 10 09:21:44 2026 /sbin/init\n 4300 synthetic R     3.1   2048 Sat Nov 22 15:40:00 2026 bash\n') % (
  1000+n*3, 1000+n*40, 100, 5000+n*60, 500+n*20, 2500+n*30, 500+n*20, 2500+n*30,
  1000+n*200, 2000+n*800, 50+n*10, 60+n*40, 100000+n*300000, 50000+n*120000)
# FIXTURE_HOLD=1: the shell reads nothing until 'release-shell'. FIXTURE_WINDOW: how much it lets the client send ahead.
release_shell=threading.Event()
class Server(paramiko.ServerInterface):
 def __init__(self):self.round=0;self.partial=False;self.destinations={}
 def get_banner(self):return (os.getenv('FIXTURE_BANNER'),'en') if os.getenv('FIXTURE_BANNER') else (None,None)
 def check_auth_password(self,user,password):
  with lock:counts['auth']+=1;counts['passwordRequests']+=1
  if os.getenv('FIXTURE_AUTH')=='keyboard-then-password':return paramiko.AUTH_SUCCESSFUL if self.partial and password=='synthetic-fresh' else paramiko.AUTH_FAILED
  if os.getenv('FIXTURE_AUTH')=='repeat-password':
   if not self.partial and password=='synthetic-only':self.partial=True;return paramiko.AUTH_PARTIALLY_SUCCESSFUL
   return paramiko.AUTH_SUCCESSFUL if self.partial and password=='synthetic-fresh' else paramiko.AUTH_FAILED
  if user!='synthetic' or password!='synthetic-only':return paramiko.AUTH_FAILED
  if os.getenv('FIXTURE_AUTH')=='partial':self.partial=True;return paramiko.AUTH_PARTIALLY_SUCCESSFUL
  return paramiko.AUTH_SUCCESSFUL
 def check_auth_publickey(self,user,pubkey):
  with lock:counts['auth']+=1;counts['keyChecks']+=1
  return paramiko.AUTH_SUCCESSFUL if user=='synthetic' and pubkey.asbytes()==userkey.asbytes() else paramiko.AUTH_FAILED
 def get_allowed_auths(self,user):
  if os.getenv('FIXTURE_AUTH')=='keyboard-then-password':return 'password' if self.partial else 'keyboard-interactive'
  if os.getenv('FIXTURE_AUTH')=='key':return 'publickey'
  if os.getenv('FIXTURE_AUTH')=='repeat-password':return 'password'
  return 'keyboard-interactive' if os.getenv('FIXTURE_AUTH') in ['keyboard','multi'] or self.partial else 'password,keyboard-interactive'
 def check_auth_interactive(self,user,submethods):
  self.round=1
  question='OTP code:' if self.partial else 'Password:'
  return paramiko.InteractiveQuery('Synthetic verification','Synthetic server challenge',(question,False))
 def check_auth_interactive_response(self,responses):
  expected='123456' if self.partial or self.round==2 else 'synthetic-only'
  if responses!=[expected]:return paramiko.AUTH_FAILED
  if os.getenv('FIXTURE_AUTH')=='keyboard-then-password':self.partial=True;return paramiko.AUTH_PARTIALLY_SUCCESSFUL
  if os.getenv('FIXTURE_AUTH')=='multi' and self.round==1:
   self.round=2;return paramiko.InteractiveQuery('Second factor','Synthetic OTP',('OTP code:',False))
  return paramiko.AUTH_SUCCESSFUL
 def check_global_request(self,kind,msg):
  with lock:counts['globals']+=1
  if os.getenv('FIXTURE_SILENT_GLOBAL')=='1':time.sleep(60)
  return False
 def check_channel_direct_tcpip_request(self,chanid,origin,destination):
  if os.getenv('FIXTURE_FORWARD')!='1' or destination[0]!='127.0.0.1':return paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
  self.destinations[chanid]=destination
  with lock:counts['forwarded']+=1
  return paramiko.OPEN_SUCCEEDED
 def check_channel_request(self,kind,chanid):return paramiko.OPEN_SUCCEEDED if kind=='session' else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
 def check_channel_pty_request(self,ch,term,w,h,pw,ph,modes):
  if odd=='close-on-request':hang_up(ch.get_transport());return False
  with lock:counts['ptyModes'].append(modes.hex() if isinstance(modes,bytes) else str(modes))
  with lock:counts["ptyRequests"].append([ch.get_id(),w,h]);shell_sizes[ch.get_id()]=(w,h)
  with lock:counts.setdefault('terms',[]).append(term.decode('ascii','replace') if isinstance(term,bytes) else str(term))
  return True
 def check_channel_env_request(self,ch,name,value):
  # FIXTURE_ACCEPT_ENV=A,B: accepted names (like OpenSSH AcceptEnv); everything else is refused.
  name=name.decode('utf-8','replace') if isinstance(name,bytes) else str(name)
  value=value.decode('utf-8','replace') if isinstance(value,bytes) else str(value)
  ok=name in [n for n in os.getenv('FIXTURE_ACCEPT_ENV','').split(',') if n]
  with lock:counts['env'].append({'channel':ch.get_id(),'name':name,'value':value,'accepted':ok,'afterShell':ch in shell_channels})
  return ok
 def check_channel_window_change_request(self,ch,w,h,pw,ph):
  with lock:counts['resizes'].append([w,h]);counts['resizesDetailed'].append([ch.get_id(),w,h]);shell_sizes[ch.get_id()]=(w,h)
  if resize_tui:draw_resize_frame(ch)
  return True
 def check_channel_shell_request(self,ch):
  if os.getenv('FIXTURE_DENY_SHELL')=='1':return False
  with lock:counts['shells']+=1
  shell_channels.append(ch)
  def echo():
   ch.send(b'\x1b[32mSYNTHETIC SSH\x1b[0m\r\n')
   if resize_tui:ch.sendall(b'\x1b[?1049h');draw_resize_frame(ch)
   try:
    if os.getenv('FIXTURE_HOLD')=='1':release_shell.wait()
    while True:
     data=ch.recv(32768)
     if not data:break
     with lock:counts['input'].append(data.hex())
     if resize_tui:
      with lock:token=counts["frameMarkers"][str(ch.get_id())];w,h=shell_sizes[ch.get_id()]
      ch.sendall(("\r\nACK %dx%d %s\r\n"%(w,h,token)).encode())
     else:ch.send(data)
   except Exception:pass
  threading.Thread(target=echo,daemon=True).start();return True
 def check_channel_exec_request(self,ch,command):
  with lock:counts['execs']+=1;counts['commands'].append(command.decode('utf-8',errors='replace'))
  if os.getenv('FIXTURE_DENY_EXEC')=='1':return False
  def respond():
   try:
    if os.getenv('FIXTURE_PERMISSION_EXEC')=='1' and b'/usr/bin/python3 -c ' in command:
     import shlex,subprocess
     args=shlex.split(command.decode());script=args[args.index('-c')+1];payload=json.loads(args[args.index('-c')+2])
     if '/usr/bin/sudo' in args and '-l' in args:
      ch.send_stderr(b'sudo: a password is required');ch.send_exit_status(1);ch.close();return
     if '/usr/bin/sudo' in args:
      marker=args[args.index('-p')+1].encode()
      with lock:counts['sudoPrompts']+=1
      if wrong_sudo_prompt or os.getenv('FIXTURE_WRONG_SUDO_PROMPT')=='1':marker=b'generic-password-prompt'
      ch.send_stderr(marker[:8]);time.sleep(.01);ch.send_stderr(marker[8:]);ch.settimeout(6)
      secret=b''
      while not secret.endswith(b'\n'):
       part=ch.recv(1024)
       if not part:break
       secret+=part
      with lock:counts['sudoPasswordBytes']+=len(secret)
      if secret!=b'synthetic-sudo-only\n':ch.send_exit_status(1);ch.close();return
      script="import os;os.geteuid=lambda:0\n"+script
     payload['path']=os.path.realpath(root.name)+payload['path']
     if '/usr/bin/sudo' in args and swap_permission_target:
      protected=os.path.join(os.path.realpath(root.name),'protected.txt');open(protected,'wb').write(b'protected');os.chmod(protected,0o644)
      os.unlink(payload['path']);os.symlink(protected,payload['path'])
     completed=subprocess.run([sys.executable,'-c',script,json.dumps(payload)],capture_output=True,timeout=5)
     if completed.stdout:ch.sendall(completed.stdout)
     if completed.stderr:ch.send_stderr(completed.stderr)
     ch.send_exit_status(completed.returncode);ch.close();return
    if b'FTS1' in command:
     with lock:n=counts['execs']
     ch.sendall(studio_monitor(n).encode());ch.send_exit_status(0);ch.close();return
    if b'kill -' in command:
     import re
     m=re.search(rb"= '([^']*)' \] \|\| exit 113; (kill -[A-Z]+ ([0-9]+))$",command)
     starts={b'4242':b'Sat Nov 22 15:39:59 2026',b'4300':b'Sat Nov 22 15:40:00 2026',b'1':b'Mon Nov 10 09:21:44 2026'}
     if not m:ch.send_stderr(b'unexpected kill form\n');ch.send_exit_status(2);ch.close();return
     pid=m.group(3)
     if starts.get(pid)!=m.group(1):ch.send_exit_status(113);ch.close();return
     with lock:counts.setdefault('kills',[]).append(m.group(2).decode())
     if pid==b'4242':ch.sendall(b'');ch.send_exit_status(0)
     else:ch.send_stderr(b'kill: (%s) - Operation not permitted\n'%pid);ch.send_exit_status(1)
     ch.close();return
    if os.getenv('FIXTURE_EXEC_SLOW')=='1':time.sleep(12)
    if os.getenv('FIXTURE_EXEC_FLOOD')=='1':ch.sendall(b'X'*(2*1024*1024))
    else:
     with lock:n=counts['execs']
     data=('FT_MONITOR1\nUPTIME\n%d.0 0\nLOAD\n0.1 0.2 0.3 1/1 1\nCPU\ncpu %d 0 10 %d 0 0 0 0 0 0\ncpu0 %d 0 10 %d 0 0 0 0 0 0\nMEM\nMemTotal: 1024 kB\nMemAvailable: 512 kB\nDISK\nFilesystem 1024-blocks Used Available Capacity Mounted on\nsynthetic 1000 400 600 40%% /\nNET\neth0: %d 0 0 0 0 0 0 0 %d 0 0 0 0 0 0 0\n' % (100+n*3,100+n*10,200+n*30,100+n*10,200+n*30,n*300,n*600))
     if os.getenv('FIXTURE_MANY_CPUS')=='1':
      extra=''.join('cpu%d %d 0 10 %d 0 0 0 0 0 0\n' % (i,100+n*(5+i%7*3),200+n*(10+i%5*5)) for i in range(1,256))
      data=data.replace('MEM\n',extra+'MEM\n')
     ch.sendall(data.encode())
    ch.send_exit_status(0);ch.close()
   except Exception:pass
  threading.Thread(target=respond,daemon=True).start();return True
 def check_channel_subsystem_request(self,ch,name):
  if odd=='close-on-request':hang_up(ch.get_transport());return False
  if name!='sftp' or os.getenv('FIXTURE_DENY_SFTP')=='1':return False
  with lock:counts['sftp']+=1
  return super().check_channel_subsystem_request(ch,name)
# Windows has no permission bits, owners or directory handles: they are kept here, by path. FIXTURE_EMULATE=1 does so
# elsewhere, to test this.
emulate=os.name=='nt' or os.getenv('FIXTURE_EMULATE')=='1'
modes={}
def mode_key(path):return os.path.normcase(os.path.abspath(path))
def attributes(st,path):
 a=paramiko.SFTPAttributes.from_stat(st)
 if emulate:
  import stat as kinds
  usual=0o777 if kinds.S_ISLNK(st.st_mode) else 0o755 if kinds.S_ISDIR(st.st_mode) else 0o644
  a.st_mode=kinds.S_IFMT(st.st_mode)|modes.get(mode_key(path),usual);a.st_uid=a.st_gid=1000
 return a
def set_mode(path,mode,fd=None):
 if emulate:modes[mode_key(path)]=mode&0o7777
 elif fd is not None:os.fchmod(fd,mode&0o7777)
 else:os.chmod(path,mode&0o7777)
def moved(old,new):
 if not emulate:return
 a,b=mode_key(old),mode_key(new)
 for k in [k for k in modes if k==b or k.startswith(b+os.sep)]:del modes[k]
 for k in [k for k in modes if k==a or k.startswith(a+os.sep)]:modes[b+k[len(a):]]=modes.pop(k)
def gone(path):
 if emulate:modes.pop(mode_key(path),None)
class Handle(paramiko.SFTPHandle):
 def stat(self):return attributes(os.fstat(self.readfile.fileno()),self.path)
 def chattr(self,attr):
  try:
   if attr.st_mode is not None:set_mode(self.path,attr.st_mode,self.readfile.fileno())
   return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
class DirectoryHandle(paramiko.SFTPHandle):
 def __init__(self,path):
  super().__init__();self.path=path;self.fd=None
  if emulate:os.listdir(path)
  else:self.fd=os.open(path,os.O_RDONLY|os.O_DIRECTORY)
 def stat(self):return attributes(os.stat(self.path) if self.fd is None else os.fstat(self.fd),self.path)
 def chattr(self,attr):
  try:
   if attr.st_mode is not None:set_mode(self.path,attr.st_mode,self.fd)
   return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def close(self):
  if self.fd is not None:os.close(self.fd)
  return super().close()
class FixtureSFTPServer(paramiko.SFTPServer):
 def _open_folder(self,request,path):
  files=self.server.list_folder(path)
  if isinstance(files,list):
   try:handle=DirectoryHandle(self.server.local(path));handle._set_files(files);self._send_handle_response(request,handle,True)
   except OSError as e:self._send_status(request,paramiko.SFTPServer.convert_errno(e.errno))
  else:self._send_status(request,files)
 def _process(self,kind,request,msg):
  from paramiko.sftp import CMD_REALPATH
  if kind==CMD_REALPATH and (os.getenv('FIXTURE_DENY_REALPATH')=='1' or os.getenv('FIXTURE_UNSUPPORTED_REALPATH')=='1'):
   code=paramiko.SFTP_PERMISSION_DENIED if os.getenv('FIXTURE_DENY_REALPATH')=='1' else paramiko.SFTP_OP_UNSUPPORTED
   self._send_status(request,code);return
  return super()._process(kind,request,msg)
 def _send_server_version(self):
  if os.getenv('FIXTURE_POSIX')!='1':return super()._send_server_version()
  from paramiko.sftp import CMD_INIT,CMD_VERSION
  kind,data=self._read_packet()
  if kind!=CMD_INIT:raise IOError('bad init')
  message=paramiko.Message();message.add_int(3);message.add('posix-rename@openssh.com','1');self._send_packet(CMD_VERSION,message)
  return struct.unpack('>I',data[:4])[0]
class Files(paramiko.SFTPServerInterface):
 def local(self,path):
  p=os.path.abspath(os.path.join(os.path.realpath(root.name),path.lstrip('/')))
  if os.path.commonpath([p,os.path.realpath(root.name)])!=os.path.realpath(root.name):raise PermissionError()
  return p
 def canonicalize(self,path):
  rel=os.path.relpath(self.local(path),os.path.realpath(root.name)).replace('\\','/')
  return '/' if rel=='.' else '/'+rel
 def list_folder(self,path):
  with lock:counts['lists'].append(path)
  if os.getenv('FIXTURE_LIST_DELAY'):time.sleep(float(os.getenv('FIXTURE_LIST_DELAY')))
  try:
   result=[]
   # FIXTURE_LSTAT_LIST=1: readdir attributes describe links themselves, as OpenSSH's sftp-server does.
   info=os.lstat if os.getenv('FIXTURE_LSTAT_LIST')=='1' else os.stat
   for name in os.listdir(self.local(path)):
    a=attributes(info(self.local(path+'/'+name)),self.local(path+'/'+name));a.filename=name;result.append(a)
   return result
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def stat(self,path):
  try:return attributes(os.stat(self.local(path)),self.local(path))
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def lstat(self,path):
  with lock:counts['lstats']+=1
  if os.getenv('FIXTURE_STAT_DELAY'):time.sleep(float(os.getenv('FIXTURE_STAT_DELAY')))
  try:return attributes(os.lstat(self.local(path)),self.local(path))
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def open(self,path,flags,attr):
  with lock:counts['openModes'].append(attr.st_mode)
  try:
   new=not os.path.exists(self.local(path));fd=os.open(self.local(path),flags,0o600)
   if new:set_mode(self.local(path),0o600,fd)
   mode='r+b' if flags&os.O_RDWR else ('wb' if flags&os.O_WRONLY else 'rb');f=os.fdopen(fd,mode)
   h=Handle(flags);h.readfile=f;h.writefile=f;h.path=self.local(path);return h
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def mkdir(self,path,attr):
  try:
   os.mkdir(self.local(path),((attr.st_mode or 0o700)&0o7777))
   if emulate:set_mode(self.local(path),attr.st_mode or 0o700)
   return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def rmdir(self,path):
  try:os.rmdir(self.local(path));gone(self.local(path));return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def remove(self,path):
  try:os.unlink(self.local(path));gone(self.local(path));return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def chattr(self,path,attr):
  try:
   if attr.st_mode is not None:set_mode(self.local(path),attr.st_mode)
   return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def posix_rename(self,old,new):
  try:
   if os.getenv('FIXTURE_COMMIT_DELAY'):time.sleep(float(os.getenv('FIXTURE_COMMIT_DELAY')))
   os.replace(self.local(old),self.local(new));moved(self.local(old),self.local(new));return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
 def rename(self,old,new):
  try:
   if os.path.exists(self.local(new)):return paramiko.SFTP_FAILURE
   os.rename(self.local(old),self.local(new));moved(self.local(old),self.local(new));return paramiko.SFTP_OK
  except OSError as e:return paramiko.SFTPServer.convert_errno(e.errno)
listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(10)
print(json.dumps({'port':listener.getsockname()[1],'fingerprint':key.fingerprint,'privateKeyPath':keypath}),flush=True)
def accept():
 while True:
  try:s,_=listener.accept()
  except OSError:return
  with lock:counts['connections']+=1
  def handle(s):
   t=paramiko.Transport(s,default_window_size=int(os.getenv('FIXTURE_WINDOW','2097152')));transports.append(t);t.add_server_key(key);t.set_subsystem_handler('sftp',FixtureSFTPServer,Files)
   try:
    server=Server();accepted_channels=[];t.start_server(server=server)
    with lock:counts['negotiated'].append({'kex':getattr(t,'fixture_kex',''),'hostKey':t.host_key_type,'cipher':t.remote_cipher})
    while t.is_active():
     if odd=='close-after-login' and t.is_authenticated():hang_up(t)
     channel=t.accept(.03)
     if channel is not None:accepted_channels[:]=[c for c in accepted_channels if not c.closed];accepted_channels.append(channel)
     if channel is not None and channel.get_id() in server.destinations:
      destination=server.destinations.pop(channel.get_id())
      def relay(channel,destination):
       try:remote=socket.create_connection(destination,timeout=5);remote.settimeout(None)
       except OSError:channel.close();return
       def copy(source,target):
        try:
         while True:
          data=source.recv(32768)
          if not data:break
          target.sendall(data)
        except Exception:pass
        finally:source.close();target.close()
       threading.Thread(target=copy,args=(channel,remote),daemon=True).start();threading.Thread(target=copy,args=(remote,channel),daemon=True).start()
      threading.Thread(target=relay,args=(channel,destination),daemon=True).start()
   except Exception:pass
   finally:t.close()
  threading.Thread(target=handle,args=(s,),daemon=True).start()
threading.Thread(target=accept,daemon=True).start()
for line in sys.stdin:
 if line.strip()=='large':
  with open(os.path.join(root.name,'a/large.bin'),'wb') as output:
   block=bytes(range(256))*128
   for _ in range(544):output.write(block)
   output.write(b'large-copy-end')
  print(json.dumps({'largeReady':True}),flush=True)
 if line.strip()=='wrong-sudo-prompt':
  wrong_sudo_prompt=True;print(json.dumps({'wrongPromptReady':True}),flush=True)
 if line.strip()=='swap-permission':
  swap_permission_target=True;print(json.dumps({'swapReady':True}),flush=True)
 if line.strip()=='seed-tree':
  os.makedirs(os.path.join(root.name,'a/nested/deep'),exist_ok=True);os.makedirs(os.path.join(root.name,'b/other'),exist_ok=True)
  print(json.dumps({'treeReady':True}),flush=True)
 if line.strip()=='seed-text':
  open(os.path.join(root.name,'edit.txt'),'wb').write('合成文件\r\nline\n'.encode('utf-8'))
  print(json.dumps({'textReady':True}),flush=True)
 if line.strip()=='make-links':
  directory=os.path.join(root.name,'real-dir');os.mkdir(directory)
  file=os.path.join(root.name,'real-file');open(file,'wb').write(b'synthetic')
  os.symlink(directory,os.path.join(root.name,'a','nested-link'));os.symlink(directory,os.path.join(root.name,'link-dir'));os.symlink(file,os.path.join(root.name,'link-file'))
  print(json.dumps({'linksReady':True}),flush=True)
 if line.strip()=='fill-real-dir':
  open(os.path.join(root.name,'real-dir','inside.txt'),'wb').write(b'inside')
  print(json.dumps({'filled':True}),flush=True)
 if line.strip()=='swap-a':
  # Replace /a by a look-alike (same owner/mode, old mtime) after the user confirmed a delete.
  os.rename(os.path.join(root.name,'a'),os.path.join(root.name,'a-moved'))
  os.makedirs(os.path.join(root.name,'a/nested/deep'))
  for d in ('a/nested/deep','a/nested','a'):os.utime(os.path.join(root.name,d),(946684800,946684800))
  print(json.dumps({'swapped':True}),flush=True)
 if line.strip()=='files':
  import hashlib
  found={}
  for directory,dirs,names in os.walk(root.name):
   for name in names:
    path=os.path.join(directory,name)
    if os.path.isfile(path) and not os.path.islink(path):
     with open(path,'rb') as f:found[os.path.relpath(path,root.name)]=hashlib.sha256(f.read()).hexdigest()
  print(json.dumps(found),flush=True)
 if line.strip()=='emit':
  for channel in list(shell_channels):
   try:channel.sendall(b'\r\nBACKGROUND OUTPUT\r\n')
   except Exception:pass
  print(json.dumps({'emitted':True}),flush=True)
 if line.strip()=='flood':
  def flood():
   try:shell_channels[0].sendall(b'X'*(6*1024*1024))
   except Exception:pass
  threading.Thread(target=flood,daemon=True).start();print(json.dumps({'floodStarted':True}),flush=True)
 if line.strip()=='release-shell':release_shell.set();print(json.dumps({'released':True}),flush=True)
 if line.strip()=='drop':
  for t in transports:hang_up(t)
  print(json.dumps({'dropped':True}),flush=True)
 if line.strip()=='close-shell':shell_channels[-1].close();print(json.dumps({'closed':True}),flush=True)
 if line.strip()=='flood-past-window':
  def flood_past_window():
   try:
    channel=shell_channels[0];end=time.time()+20
    while time.time()<end:
     with channel.lock:channel.out_window_size=1<<30
     channel.sendall(b'X'*65536)
   except Exception:pass
  threading.Thread(target=flood_past_window,daemon=True).start();print(json.dumps({'floodStarted':True}),flush=True)
 if line.strip()=='rekey':
  for t in list(transports):
   if t.is_active():t.renegotiate_keys()
  print(json.dumps({'rekeyed':True}),flush=True)
 if line.strip()=='stats':print(json.dumps(snapshot()),flush=True)
 if line.strip()=='stop':break
listener.close()

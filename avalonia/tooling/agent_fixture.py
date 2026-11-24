"""Synthetic owned ssh-agent peer; never uses SSH_AUTH_SOCK or a user agent."""
import socket,struct,sys,json,os,paramiko,threading,tempfile
folder=tempfile.mkdtemp(prefix='ft-agent-',dir='/tmp');os.chmod(folder,0o700);path=os.path.join(folder,'a.sock')
key=paramiko.PKey.from_path(sys.argv[1],passphrase='synthetic-phrase' if len(sys.argv)>2 else None)
listener=socket.socket(socket.AF_UNIX);listener.bind(path);listener.listen(5)
counts={'identities':0,'signs':0,'flags':[]};lock=threading.Lock()
def getn(ch,n):
 b=b''
 while len(b)<n:
  part=ch.recv(n-len(b))
  if not part:raise EOFError()
  b+=part
 return b
def string(b):return struct.pack('>I',len(b))+b
def peer(ch):
 try:
  while True:
   n=struct.unpack('>I',getn(ch,4))[0]
   if n<1 or n>262144:return
   msg=paramiko.Message(getn(ch,n));kind=msg.get_byte()[0]
   if kind==11:
    with lock:counts['identities']+=1
    response=b'\x0c'+struct.pack('>I',1)+string(key.asbytes())+string(b'owned synthetic key')
   elif kind==13:
    public=msg.get_string();data=msg.get_string();flags=msg.get_int()
    with lock:counts['signs']+=1;counts['flags'].append(flags)
    algorithm='ssh-rsa' if os.getenv('FIXTURE_AGENT_BAD_ALGO')=='1' else 'rsa-sha2-512' if flags&4 else 'rsa-sha2-256' if flags&2 else key.get_name()
    response=b'\x0e'+string(bytes(key.sign_ssh_data(data,algorithm))) if public==key.asbytes() else b'\x05'
   else:response=b'\x05'
   ch.sendall(struct.pack('>I',len(response))+response)
 except (EOFError,OSError):pass
 finally:ch.close()
def accept():
 while True:
  try:ch,_=listener.accept()
  except OSError:return
  threading.Thread(target=peer,args=(ch,),daemon=True).start()
threading.Thread(target=accept,daemon=True).start();print(json.dumps({'socket':path,'fingerprint':key.fingerprint}),flush=True)
try:
 for line in sys.stdin:
  if line.strip()=='stats':
   with lock:print(json.dumps(counts),flush=True)
  if line.strip()=='stop':break
finally:listener.close();os.unlink(path);os.rmdir(folder)

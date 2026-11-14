"""Local-only integration fixture; no customer data or production credentials."""
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlsplit, parse_qs
from datetime import datetime, timezone
import base64, json, socketserver, socket, threading, select

PHASE=0
USER={'id':41,'first_name':'Тестовый','last_name':'Клиент','email':'client@example.test'}
UUID='33333333-3333-4333-8333-333333333333'
class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def answer(self,obj,status=200,cookie=False):
  data=json.dumps(obj,ensure_ascii=False).encode() if not isinstance(obj,bytes) else obj
  self.send_response(status);self.send_header('Content-Type','application/json; charset=utf-8');self.send_header('Content-Length',str(len(data)))
  if cookie:self.send_header('Set-Cookie','internet_account=fixture-session; Path=/; HttpOnly; Max-Age=86400')
  self.end_headers();self.wfile.write(data)
 def do_POST(self):self.do_GET()
 def do_GET(self):
  global PHASE
  url=urlsplit(self.path);query=parse_qs(url.query);action=query.get('action',[''])[0]
  authenticated='internet_account=fixture-session' in self.headers.get('Cookie','')
  stamp=datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')
  if url.path=='/fixture/phase':PHASE=int(query.get('value',['0'])[0]);return self.answer({'phase':PHASE})
  if url.path=='/ping':return self.answer(b'',204)
  if action=='me':return self.answer({'user':USER if authenticated else None,'csrf':'fixture-csrf'})
  if action=='login':
   body=json.loads(self.rfile.read(int(self.headers.get('Content-Length',0))))
   if self.headers.get('X-CSRF-Token')!='fixture-csrf':return self.answer({'error':'CSRF'},403)
   if body.get('email')!=USER['email'] or body.get('password')!='fixture-pass':return self.answer({'error':'Invalid credentials'},401)
   return self.answer({'user':USER,'csrf':'fixture-csrf'},cookie=True)
  if action=='logout':return self.answer({'csrf':'fixture-csrf'})
  if action=='subscriptions':
   if not authenticated:return self.answer({'error':'Authentication required'},401)
   base='http://10.0.2.2:18080/sub/'
   rows=[{'id':801,'name':'VPN-A','url':base+('a2' if PHASE==1 else 'missing' if PHASE==3 else 'a'),'content_updated_at':str(PHASE)},
         {'id':803,'name':'Expired','is_expired':True,'expires_at':'2020-01-01 00:00:00'}]
   if PHASE!=1:rows.insert(1,{'id':802,'name':'VPN-B','url':base+'b','content_updated_at':'0'})
   if PHASE==2:rows=[]
   return self.answer({'subscriptions':rows})
  if url.path.startswith('/sub/'):
   name=url.path.rsplit('/',1)[1]
   if name=='missing':return self.answer({'error':'missing'},404)
   profiles=[f'vless://{UUID}@10.0.2.2:18081?encryption=none&security=none&type=tcp#'+x for x in ({'a':['A-1','A-2'],'a2':['A-NEW-1','A-NEW-2'],'b':['B-1']}[name])]
   return self.answer(base64.b64encode('\n'.join(profiles).encode()))
  if url.path.endswith('service-notice.php'):return self.answer({'enabled':True,'level':'ok','message':'Тест: сервис работает без сбоев.'})
  if url.path.endswith('server-status.php'):return self.answer({'nodes':[{'name':'Fixture server','status':'up','latency_ms':258,'checked_at':stamp}]})
  return self.answer({'error':'Not found'},404)

class VlessFixture(socketserver.BaseRequestHandler):
 """Minimal real VLESS/TCP peer, restricted to this fixture's loopback HTTP endpoint."""
 def handle(self):
  def read(n):
   data=b''
   while len(data)<n:
    part=self.request.recv(n-len(data))
    if not part:raise EOFError()
    data+=part
   return data
  self.request.settimeout(20)
  try:
   version=read(1);identity=read(16);read(read(1)[0]);command=read(1)
   port=int.from_bytes(read(2),'big');kind=read(1)[0]
   address=socket.inet_ntop(socket.AF_INET,read(4)) if kind==1 else read(read(1)[0]).decode() if kind==2 else socket.inet_ntop(socket.AF_INET6,read(16))
   if identity.hex()!=UUID.replace('-','') or command!=b'\x01' or address not in ('127.0.0.1','localhost') or port!=18080:return
   with socket.create_connection(('127.0.0.1',18080),timeout=20) as remote:
    self.request.sendall(version+b'\x00')
    while True:
     ready,_,_=select.select([self.request,remote],[],[],20)
     if not ready:return
     for source in ready:
      data=source.recv(65536)
      if not data:return
      (remote if source is self.request else self.request).sendall(data)
  except (OSError,EOFError):return

if __name__=='__main__':
 class Server(socketserver.ThreadingTCPServer):allow_reuse_address=True;daemon_threads=True
 threading.Thread(target=Server(('0.0.0.0',18081),VlessFixture).serve_forever,daemon=True).start()
 ThreadingHTTPServer(('0.0.0.0',18080),Handler).serve_forever()

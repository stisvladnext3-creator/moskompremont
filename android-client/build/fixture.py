"""Local-only integration fixture; no customer data or production credentials."""
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlsplit, parse_qs
from datetime import datetime, timezone
import base64, json, socketserver, socket, threading, select, os, re, hashlib
from pathlib import Path

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
  if url.path=='/fixture/screenshot' and self.command=='POST':
   name=query.get('name',[''])[0];size=int(self.headers.get('Content-Length',0));assert re.fullmatch(r'[a-z0-9-]+\.png',name) and 0<size<=4*1024*1024
   target=Path(os.environ.get('RUNNER_TEMP','/tmp'))/'moskom-result/review';target.mkdir(parents=True,exist_ok=True);(target/name).write_bytes(self.rfile.read(size));return self.answer({'saved':True})
  authenticated='internet_account=fixture-session' in self.headers.get('Cookie','')
  stamp=datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')
  if url.path=='/fixture/github':
   remote=urlsplit(query.get('url',[''])[0]);assets=Path(os.environ['RUNNER_TEMP'])/'moskom-android/V2rayNG/app/src/main/assets';rules=assets/'moskom/russia'
   manifest=json.loads((rules/'manifest.json').read_text());release=manifest['geoRelease'];commit='e'*40
   if remote.hostname=='api.github.com':
    if remote.path.endswith('/commits/main'):return self.answer({'sha':commit})
    if remote.path.endswith('/releases/latest'):return self.answer({'tag_name':release,'assets':[{'name':name,'browser_download_url':f'https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/download/{release}/{name}','digest':'sha256:'+hashlib.sha256((assets/name).read_bytes()).hexdigest()} for name in ['geoip.dat','geosite.dat']]})
    if remote.path.endswith('/releases'):return self.answer([])
   name=remote.path.rsplit('/',1)[1]
   if remote.hostname=='raw.githubusercontent.com' and '/'+commit+'/' in remote.path:
    mapping={'template.json':'template.json','simple_dns.json':'simple_dns.json'}
    for index,item in enumerate(json.loads((rules/'template.json').read_text())['routingItems']):mapping[item['url'].rsplit('/',1)[1]]=f'route-{index}.json'
    if name in mapping:return self.answer((rules/mapping[name]).read_bytes())
   if remote.hostname=='github.com' and remote.path.startswith('/runetfreedom/russia-v2ray-rules-dat/releases/download/'+release+'/') and name in ['geoip.dat','geosite.dat']:return self.answer((assets/name).read_bytes())
   return self.answer({'error':'Unknown fixture asset'},404)
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
   if identity.hex()!=UUID.replace('-','') or command!=b'\x01':return
   # Controlled media addresses terminate here, never on a real messenger server.
   if address in ('91.105.192.8','57.144.0.8') and port==18839:
    self.request.sendall(version+b'\x00')
    header=b''
    while b'\r\n\r\n' not in header and len(header)<8192:header+=read(1)
    body=b'moskom-media-fixture'+b'x'*65536
    self.request.sendall(b'HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: '+str(len(body)).encode()+b'\r\n\r\n'+body)
    return
   if address not in ('127.0.0.1','localhost') or port!=18080:return
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

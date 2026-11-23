#!/usr/bin/env python3
"""Run after openwrt-updater/tests/backend.py against its disposable fixture.
Real PHP/MySQL, HTTPS/curl and (with MKR_XRAY) native Xray validation.
Only the router's XKeen restart/process and Entware cron are test doubles.
"""
from pathlib import Path
import hashlib, http.cookiejar, http.server, json, os, shutil, ssl, subprocess, tempfile, threading, urllib.request, urllib.error
from agent import Fixture,UUID
BASE=os.environ.get('MKR_TEST_SITE','http://localhost:4201')
PASSWORD='fixture-only-password'
class Client:
 def __init__(self):self.csrf='';self.opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()));self.call('me')
 def call(self,action,body=None,expected=200):
  req=urllib.request.Request(BASE+'/api/account.php?action='+action,None if body is None else json.dumps(body).encode(),{'Origin':BASE,**({'Content-Type':'application/json','X-CSRF-Token':self.csrf} if body is not None else {})})
  try:
   with self.opener.open(req,timeout=15) as r:status=r.status;data=json.load(r)
  except urllib.error.HTTPError as e:status=e.code;data=json.load(e)
  self.csrf=data.get('csrf',self.csrf);assert status==expected,(action,status,data);return data
 def register(self,email):return self.call('register',{'first_name':'Keenetic','last_name':'Тест','email':email,'telegram':'+79990001111','password':PASSWORD},201)['user']['id']
def agent(token,format='',expected=200,body=None):
 req=urllib.request.Request(BASE+'/api/router-agent.php'+format,None if body is None else json.dumps(body).encode(),headers={'X-MKR-Router-Token':token,'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=15) as r:status=r.status;result=json.load(r)
 except urllib.error.HTTPError as e:status=e.code;result=json.load(e)
 assert status==expected,(status,result);return result

admin=Client();admin.call('login',{'email':'admin@example.test','password':PASSWORD})
client=Client();uid=client.register('keenetic@example.test')
other=Client();other_uid=other.register('keenetic-other@example.test')
admin.call('admin_keenetic_access',{'user_id':uid,'enabled':True})
assert client.call('keenetic')['enabled'] is True
key=f'vless://{UUID}@old.example:443?encryption=none&type=tcp#Keenetic'
for owner,label,value in [(uid,'Дом Keenetic',key),(other_uid,'Чужой роутер',key.replace(UUID,'22222222-2222-4222-8222-222222222222')),(uid,'Другой протокол','trojan://fixture-password@trojan.example:443')]:
 admin.call('admin_assign_router_key',{'user_id':owner,'label':label,'key_format':'connection','key_value':value})
sources=client.call('router_devices')['sources'];mine=[s for s in sources if s.get('xkeen_compatible')]
assert len(mine)==1 and mine[0]['label']=='Дом Keenetic' and all(s['label']!='Чужой роутер' for s in sources)
assert all('key_value' not in s and 'value' not in s for s in sources)
kid=mine[0]['id'];created=client.call('router_device_create',{'resource_type':'router_key','resource_id':kid,'label':'Keenetic SSH'},201);token=created['token']
legacy=agent(token);assert 'xray_outbound' not in legacy and legacy['resource']['value']==key
snapshot=agent(token,'?format=xray&resource_id=999999');o=snapshot['xray_outbound']
assert o['settings']['vnext'][0]['address']=='old.example' and o['settings']['vnext'][0]['port']==443
assert snapshot['resource']['revision']==hashlib.sha256(key.encode()).hexdigest()
unsupported=next(s for s in sources if s['label']=='Другой протокол')
second=client.call('router_device_create',{'resource_type':'router_key','resource_id':unsupported['id'],'label':'Manual'},201)
manual=agent(second['token'],'?format=xray');assert 'xray_error' in manual and 'xray_outbound' not in manual
other.call('router_device_create',{'resource_type':'router_key','resource_id':kid,'label':'No'},404)

class Proxy(http.server.BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def do_GET(self):self.forward()
 def do_POST(self):self.forward()
 def forward(self):
  if not self.path.startswith('/api/router-agent.php'):self.send_error(404);return
  body=self.rfile.read(min(4096,int(self.headers.get('Content-Length','0')))) if self.command=='POST' else None
  headers={k:v for k,v in self.headers.items() if k.lower() in ['content-type','x-mkr-router-token']}
  req=urllib.request.Request(BASE+self.path,body,headers,method=self.command)
  try:
   with urllib.request.urlopen(req,timeout=15) as r:status=r.status;data=r.read()
  except urllib.error.HTTPError as e:status=e.code;data=e.read()
  self.send_response(status);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)

f=Fixture();server=None
try:
 # Use real curl for this test: token stays in its private --config file.
 (f.p/'bin/curl').unlink()
 cert=f.p/'test-cert.pem';private=f.p/'test-key.pem'
 subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-nodes','-days','1','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost','-keyout',str(private),'-out',str(cert)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Proxy);ctx=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);ctx.load_cert_chain(cert,private);server.socket=ctx.wrap_socket(server.socket,server_side=True)
 threading.Thread(target=server.serve_forever,daemon=True).start()
 os.environ['CURL_CA_BUNDLE']=str(cert)
 endpoint=f'https://localhost:{server.server_port}/api/router-agent.php'
 f.call('setup',endpoint,input=token+'\n');assert f.state()=='current' and f.count('restart')==0
 plan=admin.call('admin_router_host_preview',{'new_host':'rotated.example','old_host':'old.example'})
 assert plan['changed']>=2  # Includes matching records left by the base fixture.
 admin.call('admin_router_host_apply',{'preview_token':plan['preview_token']})
 agent(token,expected=409,body={'state':'applied','revision':snapshot['resource']['revision'],'client_version':'0.1.1'})
 f.call('check');assert f.state()=='applied' and f.host()=='rotated.example' and f.count('restart')==1
 data=agent(token,'?format=xray');assert data['xray_outbound']['settings']['vnext'][0]['address']=='rotated.example'
 f.call('check');assert f.state()=='current' and f.count('restart')==1
 metadata=client.call('router_devices');row=next(x for x in metadata['devices'] if x['id']==created['id']);assert row['last_state']=='current' and row['last_seen_at'] and row['client_version']=='0.1.1'
 assert token not in json.dumps(metadata) and 'vless://' not in json.dumps(metadata)
 # Real HTTP authorization refusal leaves the applied file in place.
 before=f.file.read_bytes();client.call('router_device_revoke',{'id':created['id']});f.call('check',code=1);assert f.file.read_bytes()==before and f.state()=='manual'
finally:
 if server:server.shutdown();server.server_close()
 os.environ.pop('CURL_CA_BUNDLE',None);f.close()
print('PASS: real PHP/MySQL + TLS/curl + Xray validation; own VLESS source, legacy API, private device scope, key/domain rotation -> atomic outbound update -> native restart invocation -> server state, no-op poll, token revocation keeps current file.',flush=True)

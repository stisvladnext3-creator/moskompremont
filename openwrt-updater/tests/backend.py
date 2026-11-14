#!/usr/bin/env python3
"""HTTP integration against an isolated PHP/MySQL cabinet, never the production site."""
from pathlib import Path
import hashlib, http.cookiejar, json, os, subprocess, urllib.request, urllib.error
BASE=os.environ.get('MKR_TEST_SITE','http://localhost:4201');DB=os.environ.get('MKR_TEST_DB','mkr-v21-db')
D=['env','-u','DOCKER_HOST','-u','DOCKER_CONTEXT','-u','DOCKER_TLS','-u','DOCKER_TLS_VERIFY','-u','DOCKER_CERT_PATH','docker']
if os.environ.get('MKR_DOCKER_SOCKET'):D+=['--host='+os.environ['MKR_DOCKER_SOCKET']]
ROOT=Path(__file__).resolve().parents[2]
PASSWORD='fixture-only-password'
def sql(text,out=False):
 r=subprocess.run(D+['exec','-i',DB,'mariadb','-N','-B','-uroot','internet_test'],input=text.encode(),capture_output=True,check=True);return r.stdout.decode().strip() if out else None
class Client:
 def __init__(self):self.csrf='';self.opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()));self.call('me')
 def call(self,action,body=None,expected=200,csrf=True):
  request=urllib.request.Request(BASE+'/api/account.php?action='+action,None if body is None else json.dumps(body).encode(),{'Origin':BASE,**({'Content-Type':'application/json','X-CSRF-Token':self.csrf if csrf else ''} if body is not None else {})})
  try:
   with self.opener.open(request,timeout=15) as r:status=r.status;result=json.load(r)
  except urllib.error.HTTPError as e:status=e.code;result=json.load(e)
  self.csrf=result.get('csrf',self.csrf);assert status==expected,(action,status,result);return result
 def register(self,email):return self.call('register',{'first_name':'Тест','last_name':'Клиент','email':email,'telegram':'+79990001111','password':PASSWORD},201)
def agent(token=None,body=None,expected=200,query=''):
 req=urllib.request.Request(BASE+'/api/router-agent.php'+query,None if body is None else json.dumps(body).encode(),{'Content-Type':'application/json',**({'X-MKR-Router-Token':token} if token else {})})
 try:
  with urllib.request.urlopen(req,timeout=15) as r:status=r.status;data=json.load(r);assert 'no-store' in r.headers['Cache-Control']
 except urllib.error.HTTPError as e:status=e.code;data=json.load(e)
 assert status==expected,(status,expected,data);return data
schema=(ROOT/'module/private/schema.sql').read_text().split('-- Коды устройств OpenWrt:')[0];sql(schema)
admin=Client();admin.register('admin@example.test');alice=Client();alice.register('alice@example.test');bob=Client();bob.register('bob@example.test')
alice.call('router_devices',expected=503)
migration=(ROOT/'module/private/migrations/011-router-devices.sql').read_text();sql(migration);sql(migration)
admin.call('import_subscriptions',{'csv':'name,url\nALICE,https://test.example/sub/alice\nBOB,https://test.example/sub/bob\n'})
alice.call('claim_subscriptions',{'names':'ALICE'});bob.call('claim_subscriptions',{'names':'BOB'})
aid=int(alice.call('subscriptions')['subscriptions'][0]['id']);bid=int(bob.call('subscriptions')['subscriptions'][0]['id'])
key='vless://11111111-1111-4111-8111-111111111111@old.example:443?encryption=none&type=tcp#Router'
admin.call('admin_assign_router_key',{'user_id':2,'label':'Router','key_format':'connection','key_value':key})
kid=int(alice.call('router_keys')['keys'][0]['id'])
def create(client=alice,typ='subscription',rid=None,expected=201,**extra):return client.call('router_device_create',{'label':'OpenWrt test','resource_type':typ,'resource_id':aid if rid is None else rid,**extra},expected)
created=create(user_id=3);token=created['token'];did=created['id'];assert agent(token)['resource']['id']==aid
assert agent(token,query='?resource_id='+str(bid))['resource']['id']==aid
agent(expected=401);agent(token+'a',expected=401);agent('mkr1.'+'1'*24+'.'+'2'*64,expected=401)
agent(expected=401,query='?token='+token)
create(rid=bid,expected=404);create(typ='invalid',expected=404)
alice.call('admin_router_devices',expected=403);alice.call('admin_router_device_create',{'user_id':3,'resource_type':'subscription','resource_id':bid,'label':'No'},403)
alice.call('router_device_create',{'resource_type':'subscription','resource_id':aid,'label':'No CSRF'},403,csrf=False)
bob.call('router_device_revoke',{'id':did},404)
metadata=json.dumps(alice.call('router_devices'));assert token not in metadata and 'token_hash' not in metadata and 'test.example/sub/alice' not in metadata
assert sql('SELECT token_hash FROM internet_router_devices WHERE id='+str(did),True)==hashlib.sha256(token.split('.')[2].encode()).hexdigest()
# Updating the underlying record keeps device identity/token, and notifies only its owner.
new='https://new.example/sub/alice';admin.call('import_subscriptions',{'csv':'name,url\nALICE,'+new+'\nBOB,https://test.example/sub/bob\n'});assert agent(token)['resource']['value']==new
revision=hashlib.sha256(new.encode()).hexdigest();report={'state':'applied','revision':revision,'client_version':'0.1.0'}
agent(token,report);agent(token,report);assert sql("SELECT COUNT(*) FROM internet_client_notifications WHERE event_key='router-device:"+str(did)+':'+revision+":applied'",True)=='1'
agent(token,{**report,'state':'execute'},400);assert sql('SELECT url FROM internet_subscriptions WHERE id='+str(aid),True)==new
# Expiry blocks a subscription link without revoking the device; renewal restores access.
admin.call('admin_subscription_term_save',{'id':aid,'user_id':2,'mode':'until','expires_at':'2020-01-01T00:00:00Z'});agent(token,expected=410)
admin.call('admin_subscription_term_save',{'id':aid,'user_id':2,'mode':'until','expires_at':'2030-01-01T00:00:00Z'});assert agent(token)['resource']['value']==new
alice.call('router_device_revoke',{'id':did});agent(token,expected=401)
# Administrator can pair one of a chosen client's keys and revoke it.
created=admin.call('admin_router_device_create',{'user_id':2,'resource_type':'router_key','resource_id':kid,'label':'Admin pair'},201);assert agent(created['token'])['resource']['value']==key
assert created['id'] in [int(x['id']) for x in admin.call('admin_router_devices')['devices']]
admin.call('admin_unassign_router_key',{'id':kid,'user_id':2});agent(created['token'],expected=401)
# Ownership changes and direct database password changes invalidate access, even without hooks.
created=create();sql('UPDATE internet_subscriptions SET user_id=3 WHERE id='+str(aid));agent(created['token'],expected=410);sql('UPDATE internet_subscriptions SET user_id=2 WHERE id='+str(aid));agent(created['token'],expected=401)
created=create();admin.call('admin_account_password',{'id':2,'password':'new-fixture-password'});agent(created['token'],expected=401)
# Deleted account cannot keep polling. Bob's device is unaffected by Alice's reset.
created=create(client=bob,rid=bid);assert agent(created['token'])['resource']['id']==bid
admin.call('admin_account_delete',{'id':3,'resources':'release'});agent(created['token'],expected=401)
# Schema/list contain no plaintext reusable token, and no website/session auth is accepted.
assert token not in sql('SELECT * FROM internet_router_devices',True)
print('PASS: migration twice, scoped access, CSRF/roles/IDOR, hashed credentials, CSV/URL updates, deduplicated notifications, expiry/renewal, revoke, key unassignment, ownership/password/account invalidation.',flush=True)

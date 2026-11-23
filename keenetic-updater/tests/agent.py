#!/usr/bin/env python3
"""Functional shell tests. API + XKeen process actions are isolated test doubles.
JSON operations, filesystem transactions, locks, cron editing are real.
Set MKR_XRAY to test generated outbound configs with a native Xray binary too.
"""
from pathlib import Path
import copy, hashlib, json, os, shlex, shutil, subprocess, tempfile
ROOT=Path(__file__).resolve().parents[1]
TOKEN='mkr1.'+'1'*24+'.'+'2'*64
UUID='11111111-1111-4111-8111-111111111111'

STUB='''#!/usr/bin/env python3
import json,os,pathlib,sys,subprocess
p=pathlib.Path(os.environ['MKR_PREFIX']);name=pathlib.Path(sys.argv[0]).name
def setting(n,d=False):
 f=p/('test-'+n);return f.read_text().strip() if f.exists() else d
def count(n):
 f=p/('count-'+n);f.write_text(str(int(f.read_text())+1 if f.exists() else 1))
if name=='curl':
 assert not any('mkr1.' in a for a in sys.argv)
 a=sys.argv;dest=pathlib.Path(a[a.index('-o')+1]);cfg=pathlib.Path(a[a.index('--config')+1])
 assert cfg.stat().st_mode&0o077==0
 assert '--proto' in a and a[a.index('--proto')+1]=='=https'
 if '--data-binary' in a:
  report=json.loads(pathlib.Path(a[a.index('--data-binary')+1][1:]).read_text())
  with (p/'reports').open('a') as f:f.write(json.dumps(report)+'\\n')
  dest.write_text('{}');print('200',end='')
 else:
  dest.write_text((p/'response.json').read_text());print(setting('http','200'),end='')
elif name=='pidof':sys.exit(0)
elif name=='xkeen':
 assert os.environ.get('XKEEN_FOREGROUND')=='1';count('restart')
 try:os.fstat(9)
 except OSError:pass
 else:raise Exception('lock descriptor leaked into XKeen')
 if setting('fail-restart'):
  (p/'test-fail-restart').unlink();sys.exit(1)
elif name=='xray':
 assert 'XRAY_LOCATION_CONFDIR' not in os.environ and 'XRAY_LOCATION_CONFIG' not in os.environ
 count('validate');candidate=pathlib.Path(sys.argv[-1]);value=json.loads(candidate.read_text())
 assert 'inbounds' not in value and 'routing' not in value
 if setting('edit-during-test'):
  target=pathlib.Path(setting('edit-during-test'));target.write_text(target.read_text()+'\\n ');(p/'test-edit-during-test').unlink()
 if setting('fail-test'):sys.exit(1)
 native=os.environ.get('MKR_XRAY')
 if native:sys.exit(subprocess.call([native,*sys.argv[1:]],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL))
elif name=='crontab':
 f=p/'crontab'
 if sys.argv[1]=='-l':
  if f.exists():print(f.read_text(),end='')
  else:sys.exit(1)
 else:f.write_bytes(pathlib.Path(sys.argv[1]).read_bytes())
'''

class Fixture:
 def __init__(self):
  self.temp=tempfile.TemporaryDirectory(prefix='mkr-xkeen-test-');self.p=Path(self.temp.name)/'opt'
  shutil.copytree(ROOT/'files/opt',self.p);self.cfg=self.p/'etc/xray/configs';self.cfg.mkdir(parents=True)
  for name in ['curl','pidof','xkeen','xray','crontab']:
   f=self.p/'bin'/name;f.write_text(STUB);f.chmod(0o755)
  if os.environ.get('MKR_BUSYBOX'):
   for name in ['awk','cp','mv','cmp','wc','date','mktemp','chmod','mkdir','rm','sleep','sync']:(self.p/'bin'/name).symlink_to(os.environ['MKR_BUSYBOX'])
  if os.environ.get('MKR_TEST_JQ'):(self.p/'bin/jq').symlink_to(os.environ['MKR_TEST_JQ'])
  (self.p/'bin/mkr-xkeen').chmod(0o755)
  self.file=self.cfg/'04_inbound.json'
  self.old={'outbounds':[{'tag':'proxy','protocol':'vless','settings':{'vnext':[{'address':'old.example','port':443,'users':[{'id':UUID,'encryption':'none'}]}]},'streamSettings':{'network':'tcp','security':'none','sockopt':{'mark':255,'tcpFastOpen':True}},'mux':{'enabled':False}}, {'tag':'direct','protocol':'freedom'}, {'tag':'block','protocol':'blackhole'}]}
  self.file.write_text('// A comment with https://example.test\n'+json.dumps(self.old,indent=2).replace('"outbounds": [','"outbounds": [ /* comment */').replace('\n  ]','\n  ,]'))
  (self.cfg/'03_inbounds.json').write_text('{"inbounds":[],"note":"http://example.test//url/*text*/"}')
  (self.cfg/'05_routing.json').write_text('{"routing":{"rules":[]}}')
  self.unchanged={p.name:p.read_bytes() for p in self.cfg.iterdir() if p!=self.file}
  self.response('new.example')
 def response(self,host='new.example',uuid=UUID):
  uri=f'vless://{uuid}@{host}:443?encryption=none&type=tcp#Test'
  self.rev=hashlib.sha256(uri.encode()).hexdigest()
  outbound={'protocol':'vless','settings':{'vnext':[{'address':host,'port':443,'users':[{'id':uuid,'encryption':'none'}]}]},'streamSettings':{'network':'tcp','security':'none'}}
  self.payload={'version':1,'device_id':1,'resource':{'id':1,'type':'router_key','label':'Router','value':uri,'revision':self.rev},'xray_outbound':outbound}
  (self.p/'response.json').write_text(json.dumps(self.payload))
 def call(self,*args,code=0,input=None):
  r=subprocess.run([*shlex.split(os.environ.get('MKR_TEST_SHELL','')),str(self.p/'bin/mkr-xkeen'),*args],input=input,text=True,capture_output=True,env={**os.environ,'MKR_PREFIX':str(self.p)},timeout=30)
  assert r.returncode==code,(args,r.returncode,r.stdout,r.stderr)
  assert TOKEN not in r.stdout+r.stderr
  for name,data in self.unchanged.items():assert (self.cfg/name).read_bytes()==data,name
  return r
 def setup(self,code=0):return self.call('setup',code=code,input=TOKEN+'\n')
 def count(self,name):
  f=self.p/('count-'+name);return int(f.read_text()) if f.exists() else 0
 def state(self):return json.loads((self.p/'var/lib/mkr-xkeen/status.json').read_text())['state']
 def host(self):return json.loads(self.file.read_text())['outbounds'][0]['settings']['vnext'][0]['address']
 def flag(self,name,value='1'):(self.p/('test-'+name)).write_text(value)
 def close(self):self.temp.cleanup()

def basic(f):
 f.setup();assert f.state()=='applied' and f.host()=='new.example';o=json.loads(f.file.read_text())['outbounds']
 assert o[0]['streamSettings']['sockopt']==f.old['outbounds'][0]['streamSettings']['sockopt']
 assert o[1:]==f.old['outbounds'][1:] and o[0]['tag']=='proxy' and o[0]['mux']=={'enabled':False}
 assert f.count('restart')==1
 f.call('check');assert f.state()=='current' and f.count('restart')==1
 status=f.call('status').stdout;assert TOKEN not in status and 'vless://' not in status
 for p in [f.p/'etc/mkr-xkeen/config.json',f.p/'var/lib/mkr-xkeen/latest.json']:assert p.stat().st_mode&0o077==0
 f.call('mode','notify');f.response('next.example');before=f.file.read_bytes();f.call('check');assert f.state()=='available' and f.file.read_bytes()==before
 f.call('mode','auto');f.call('check');assert f.host()=='next.example'
 f.call('rollback');assert f.host()=='new.example' and json.loads((f.p/'etc/mkr-xkeen/config.json').read_text())['mode']=='notify'
 f.call('unlink');assert '# mkr-xkeen-updater' not in (f.p/'crontab').read_text() and f.host()=='new.example'

def invalid_native(f):
 before=f.file.read_bytes();f.flag('fail-test');f.setup(code=1);assert f.file.read_bytes()==before and f.state()=='manual'
 f.call('check',code=1);assert f.count('validate')==1
 (f.p/'test-fail-test').unlink();f.call('retry');assert f.host()=='new.example'

def failed_restart(f):
 before=f.file.read_bytes();f.flag('fail-restart');f.setup(code=1)
 assert f.file.read_bytes()==before and f.state()=='failed' and f.count('restart')==2
 assert json.loads((f.p/'etc/mkr-xkeen/config.json').read_text())['mode']=='notify'

def concurrent_edit(f):
 before=f.file.read_bytes();f.flag('edit-during-test',str(f.file));f.setup(code=1)
 assert f.file.read_bytes()==before+b'\n ' and f.state()=='manual' and f.count('restart')==0

def multiple(f):
 a=copy.deepcopy(f.old);a['outbounds'][0]['tag']='second';f.file.write_text(json.dumps(f.old))
 other=f.cfg/'06_extra.json';other.write_text(json.dumps(a));f.response(uuid='22222222-2222-4222-8222-222222222222')
 before=f.file.read_bytes();f.setup(code=1);assert f.file.read_bytes()==before and f.state()=='manual'
 f.call('target',str(f.file),'proxy');f.call('check');assert f.host()=='new.example' and json.loads(other.read_text())==a

def uuid_match(f):
 other=copy.deepcopy(f.old);other['outbounds'][0]['tag']='second';other['outbounds'][0]['settings']['vnext'][0]['users'][0]['id']='22222222-2222-4222-8222-222222222222'
 extra=f.cfg/'06_extra.json';extra.write_text(json.dumps(other));f.setup();assert f.host()=='new.example' and json.loads(extra.read_text())==other

def revoked(f):
 f.setup();before=f.file.read_bytes();f.response('denied.example');f.flag('http','401');f.call('check',code=1)
 assert f.file.read_bytes()==before and f.state()=='manual'

def malformed(f):
 f.file.write_text('{"outbounds": [ // unterminated\n');before=f.file.read_bytes();f.setup(code=1);assert f.file.read_bytes()==before

def cron(f):
 original='15 3 * * * /opt/bin/other-job\n';(f.p/'crontab').write_text(original)
 f.setup();f.call('schedule');c=(f.p/'crontab').read_text();assert c.startswith(original) and c.count('# mkr-xkeen-updater')==1 and TOKEN not in c
 f.call('unlink');f.call('schedule');assert (f.p/'crontab').read_text()==original

def recovery(f):
 f.setup();f.response('next.example');old=f.file.read_bytes();old_hash=hashlib.sha256(old).hexdigest()
 altered=json.loads(old);altered['outbounds'][0]['settings']['vnext'][0]['address']='interrupted.example';new=json.dumps(altered).encode()
 state=f.p/'var/lib/mkr-xkeen';(state/'previous.conf').write_bytes(old)
 (state/'transaction.json').write_text(json.dumps({'file':str(f.file),'old_hash':old_hash,'new_hash':hashlib.sha256(new).hexdigest(),'revision':f.rev}))
 f.file.write_bytes(new);f.call('check');assert f.file.read_bytes()==old and f.state()=='available'
 assert not (state/'transaction.json').exists()

def unsupported(f):
 f.payload.pop('xray_outbound');f.payload['xray_error']='Manual';(f.p/'response.json').write_text(json.dumps(f.payload));before=f.file.read_bytes()
 f.setup(code=1);assert f.file.read_bytes()==before and f.state()=='manual'

def lock(f):
 f.setup()
 import fcntl
 with (f.p/'var/lib/mkr-xkeen/lock').open('w') as h:
  fcntl.flock(h,fcntl.LOCK_EX|fcntl.LOCK_NB);f.call('check',code=1)

def validation(f):
 f.call('mode','notify');before=f.file.read_bytes()
 valid=['https://moskompremont.ru/api/router-agent.php','https://example.test/internet/api/router-agent.php','https://192.0.2.1:8443/api/router-agent.php','https://[2001:db8::1]:8443/api/router-agent.php']
 for endpoint in valid:f.call('setup',endpoint,input=TOKEN+'\n')
 saved=(f.p/'etc/mkr-xkeen/config.json').read_bytes()
 for endpoint in ['http://example.test/api/router-agent.php','https:///api/router-agent.php','https://example.test/','https://user:pass@example.test/api/router-agent.php','https://example.test/api/router-agent.php?token=invalid','https://example.test/api/router-agent.php#fragment','https://example.test/../api/router-agent.php','https://example.test/api/%0Arouter-agent.php','https://example.test/api/router-agent.php\n','https://example.test/$(command)']:
  f.call('setup',endpoint,input=TOKEN+'\n',code=1);assert (f.p/'etc/mkr-xkeen/config.json').read_bytes()==saved
 for token in [TOKEN[:-1],TOKEN+'0',TOKEN.replace('mkr1.','mkr2.',1),TOKEN.replace('1'*24,'A'*24),TOKEN+'\r',TOKEN+'.extra','mkr1.'+'1'*23+'.'+'2'*64,'mkr1.'+'1'*24+'.'+'2'*63+'z']:
  r=f.call('setup',input=token+'\n',code=1);assert token not in r.stdout+r.stderr and (f.p/'etc/mkr-xkeen/config.json').read_bytes()==saved
 assert f.file.read_bytes()==before and f.count('restart')==0

def stored_bad_token(f):
 f.setup();before=f.file.read_bytes();conf=f.p/'etc/mkr-xkeen/config.json';data=json.loads(conf.read_text())
 for value in ['not-a-token',None,123,[],TOKEN+'\r\nX-Injected: secret']:
  conf.write_text(json.dumps({**data,'token':value}));r=f.call('check',code=1)
  assert 'ONIGURUMA' not in r.stderr and 'unbound' not in r.stderr and f.file.read_bytes()==before and f.state()=='failed'

def invalid_revision(f):
 f.setup();before=f.file.read_bytes()
 for revision in ['a'*63,'A'*64,'a'*63+'z',None,123]:
  response=copy.deepcopy(f.payload);response['resource']['revision']=revision;(f.p/'response.json').write_text(json.dumps(response))
  f.call('check',code=1);assert f.file.read_bytes()==before and f.state()=='failed'

def tty_prompt(f):
 import pty,select,time
 pid,master=pty.fork()
 if pid==0:
  args=[*shlex.split(os.environ.get('MKR_TEST_SHELL','')),str(f.p/'bin/mkr-xkeen'),'setup']
  os.execvpe(args[0],args,{**os.environ,'MKR_PREFIX':str(f.p)})
 output=b'';sent=False;deadline=time.monotonic()+20;finished=False
 try:
  while time.monotonic()<deadline:
   if select.select([master],[],[],.2)[0]:
    try:chunk=os.read(master,65536)
    except OSError:break
    if not chunk:break
    output+=chunk
    if not sent and 'Код устройства (ввод скрыт): '.encode() in output:
     os.write(master,(TOKEN+'\n').encode());sent=True
   if os.waitpid(pid,os.WNOHANG)[0]:finished=True;break
  if not finished:
   done,status=os.waitpid(pid,os.WNOHANG)
   if not done:os.kill(pid,9);os.waitpid(pid,0);raise AssertionError('Setup did not finish after its TTY prompt')
   assert os.waitstatus_to_exitcode(status)==0
  assert sent and TOKEN.encode() not in output and f.state()=='applied'
 finally:os.close(master)

if __name__=='__main__':
 for test in [basic,invalid_native,failed_restart,concurrent_edit,multiple,uuid_match,revoked,malformed,cron,recovery,unsupported,lock,validation,stored_bad_token,invalid_revision,tty_prompt]:
  f=Fixture()
  try:test(f);print('PASS',test.__name__,flush=True)
  finally:f.close()

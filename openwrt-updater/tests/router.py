#!/usr/bin/env python3
"""Actual OpenWrt/UCI tests in isolated x86_64 rootfs containers; services/remote server are fixtures."""
from pathlib import Path
import hashlib, io, json, os, subprocess, tarfile, tempfile
ROOT=Path(__file__).resolve().parents[1]
D=['env','-u','DOCKER_HOST','-u','DOCKER_CONTEXT','-u','DOCKER_TLS','-u','DOCKER_TLS_VERIFY','-u','DOCKER_CERT_PATH','docker']
if os.environ.get('MKR_DOCKER_SOCKET'):D+=['--host='+os.environ['MKR_DOCKER_SOCKET']]
TOKEN='mkr1.'+'a'*24+'.'+'b'*64
OLD='vless://11111111-1111-4111-8111-111111111111@old.example:443?encryption=none&security=tls&sni=example.org&type=tcp#Home'
NEW=OLD.replace('old.example','new.example')
class Router:
 def __init__(self,version):self.name='mkr-test-'+version;self.version=version
 def run(self,*args,input=None,ok=True):
  p=subprocess.run(D+['exec','-i','-e','PATH=/tmp/mkr-mocks:/usr/sbin:/usr/bin:/sbin:/bin',self.name]+list(args),input=None if input is None else input.encode(),capture_output=True)
  if ok and p.returncode:raise AssertionError((args,p.returncode,p.stdout.decode(),p.stderr.decode()))
  return p
 def sh(self,script,**kw):return self.run('sh','-c',script,**kw).stdout.decode().strip()
 def put(self,path,data,mode=0o600):
  content=data.encode() if isinstance(data,str) else data
  blob=io.BytesIO()
  with tarfile.open(fileobj=blob,mode='w') as t:
   i=tarfile.TarInfo(path.lstrip('/'));i.size=len(content);i.mode=mode;t.addfile(i,io.BytesIO(content))
  subprocess.run(D+['exec','-i',self.name,'tar','-xf','-','-C','/'],input=blob.getvalue(),check=True,capture_output=True)
 def cli(self,command,body=None,ok=True):return self.run('/usr/bin/mkr-router',command,input=None if body is None else json.dumps(body),ok=ok)
 def status(self):return json.loads(self.cli('status').stdout)
 def source(self,value=NEW,typ='router_key',device=7):self.put('/tmp/snapshot.json',json.dumps({'version':1,'device_id':device,'resource':{'id':3,'type':typ,'label':'Router fixture','value':value,'revision':hashlib.sha256(value.encode()).hexdigest()}}))
 def reset(self,version='0.6.1',mode='auto',extra=''):
  self.sh('rm -rf /tmp/mkr-router /etc/mkr-router /tmp/.uci /tmp/podkop-on /tmp/restarts /tmp/check-fail /tmp/restart-fail /tmp/http-code /tmp/curl-fail /tmp/dns-edit /tmp/sub-auth-leak /tmp/argv-leak /tmp/podkop-starting /tmp/without-routing /tmp/routing-only /tmp/no-package-version /tmp/last-check-path /usr/lib/podkop/constants.sh; mkdir -p /tmp/.uci /etc/sing-box /usr/lib/podkop')
  self.put('/etc/config/mkr_router',(ROOT/'files/etc/config/mkr_router').read_bytes())
  if version.startswith('0.7.'):
   config=(ROOT/'tests/fixtures/podkop-0.7.22.conf').read_text()
   assert config.count("option proxy_string ''")==1
   self.put('/etc/config/podkop',config.replace("option proxy_string ''","option proxy_string '"+OLD+"'")+"\n"+extra)
  else:self.put('/etc/config/podkop',"config main 'main'\n option mode 'proxy'\n option proxy_config_type 'url'\n option proxy_string '"+OLD+"'\n option dns_server '1.1.1.1'\n"+extra)
  self.put('/etc/sing-box/config.json','{}');self.put('/tmp/podkop-version',version);self.source()
  self.cli('configure',{'mode':mode,'routing_mode':'blocked','interval':900,'section':'','endpoint':'https://site.example/api/router-agent.php','token':TOKEN})
 def value(self):return self.sh('uci -q get podkop.main.proxy_string')
 def install_fixtures(self):
  self.sh('mkdir -p /tmp/mkr-mocks')
  self.put('/tmp/mkr-mocks/curl','''#!/bin/sh
conf='';out='';post=''
for argument in "$@"; do case "$argument" in *mkr1.*|*vless://*) touch /tmp/argv-leak;; esac; done
while [ "$#" -gt 0 ]; do case "$1" in --config) conf="$2";shift;; --output) out="$2";shift;; --data-binary) post="$2";shift;; esac;shift;done
[ -f /tmp/curl-fail ] && exit 7
url="$(sed -n 's/^url = //p' "$conf" | jq -r '.')"
if [ "$url" = https://site.example/api/router-agent.php ]; then
 grep -q X-MKR-Router-Token "$conf" || exit 90
 if [ -n "$post" ];then cp "${post#@}" /tmp/last-report.json; printf '{}' > "$out";printf 200
 else cp /tmp/snapshot.json "$out";if [ -f /tmp/http-code ];then cat /tmp/http-code;else printf 200;fi;fi
else
 grep -q X-MKR-Router-Token "$conf" && touch /tmp/sub-auth-leak
 cp /tmp/subscription.txt "$out";printf 200
fi
''',0o755)
  for command in ['opkg','apk']:
   real=self.sh('command -v '+command,ok=False)
   if real:self.put('/tmp/mkr-mocks/'+command,'#!/bin/sh\nif [ "$1" = status ] && [ "$2" = podkop ];then [ ! -f /tmp/no-package-version ] || exit 1;printf "Version: ";cat /tmp/podkop-version;elif [ "$1" = info ] && [ "$2" = -e ] && [ "$3" = -v ] && [ "$4" = podkop ];then [ ! -f /tmp/no-package-version ] || exit 1;printf "podkop-%s-r1\\n" "$(cat /tmp/podkop-version)";elif [ "$1" = info ] && [ "$2" = -v ] && [ "$3" = podkop ];then printf "podkop: Package description, not a version\\n";else exec '+real+' "$@";fi\n',0o755)
  self.put('/etc/init.d/podkop','''#!/bin/sh
case "$1" in
 running) [ -f /tmp/podkop-starting ];;
 restart)
  echo restart >> /tmp/restarts
  rm -f /tmp/podkop-on
  if [ -f /tmp/dns-edit ];then uci -q set podkop.main.dns_server=9.9.9.9;uci -q commit podkop;rm /tmp/dns-edit;fi
  [ -f /tmp/restart-fail ] && { rm /tmp/restart-fail;exit 1; }
  touch /tmp/podkop-on;;
esac
''',0o755)
  self.put('/tmp/mkr-mocks/sing-box','#!/bin/sh\nwhile [ "$#" -gt 0 ];do if [ "$1" = -c ];then printf "%s" "$2" > /tmp/last-check-path;[ -f "$2" ] || exit 1;shift;fi;shift;done\n[ ! -f /tmp/check-fail ]\n',0o755)
  self.put('/etc/init.d/sing-box','#!/bin/sh\n[ "$1" = running ] && [ -f /tmp/podkop-on ]\n',0o755)
  self.put('/tmp/mkr-mocks/nft','#!/bin/sh\n[ ! -f /tmp/without-routing ] && { [ -f /tmp/podkop-on ] || [ -f /tmp/routing-only ]; }\n',0o755)
  self.put('/tmp/mkr-mocks/sleep','#!/bin/sh\n# Simulated retry timer; no production code bypass.\nexit 0\n',0o755)
 def tests(self):
  self.install_fixtures()
  for version in ['0.3.4','0.4.11','0.5.8','0.6.1','0.7.22']:
   self.reset(version);self.cli('check');assert self.value()==NEW;assert self.status()['state']=='applied';assert self.sh('test ! -f /tmp/restarts && echo yes')=='yes'
   self.cli('check');assert self.status()['state']=='current';assert TOKEN not in self.cli('status').stdout.decode();assert self.status()['podkop_version']==version
  # Podkop-native routing policies are applied without replacing the proxy URL.
  self.reset('0.7.22');self.cli('configure',{'mode':'auto','routing_mode':'all','interval':900,'section':'main','endpoint':'https://site.example/api/router-agent.php','token':''});self.cli('check');assert self.value()==NEW and '0.0.0.0/0' in self.sh('uci -q get podkop.main.fully_routed_ips');assert self.status()['settings']['routing_mode']=='all'
  self.cli('configure',{'mode':'auto','routing_mode':'blocked','interval':900,'section':'main','endpoint':'https://site.example/api/router-agent.php','token':''});self.cli('check');assert self.value()==NEW and '0.0.0.0/0' not in self.sh('uci -q get podkop.main.fully_routed_ips || :') and self.sh('uci -q get podkop.main.community_lists')=='russia_inside'
  self.reset('0.7.22');self.cli('configure',{'mode':'auto','routing_mode':'except_ru','interval':900,'section':'main','endpoint':'https://site.example/api/router-agent.php','token':''});self.cli('check');assert self.value()==NEW and self.sh('uci -q get podkop.main.user_subnet_list_type')=='text' and '0.0.0.0/0' in self.sh('uci -q get podkop.main.user_subnets_text');assert self.sh('uci -q get podkop.mkr_router_main.connection_type')=='exclusion';assert self.sh('uci -q get podkop.mkr_router_main.mode')=='exclusion';assert self.sh('uci -q get podkop.mkr_router_main.user_domain_list_type')=='text';assert self.sh('uci -q get podkop.mkr_router_main.user_domains_text')=='ru xn--p1ai';assert self.sh('uci -q get podkop.mkr_router_main.remote_subnet_lists')=='https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-ru.srs';assert '.srs' in self.sh('uci -q get podkop.mkr_router_main.remote_subnet_lists')
  # A failed refresh must restore the previously active routing policy along with the URI.
  self.source(OLD);self.sh('touch /tmp/restart-fail');self.cli('check',ok=False);rollback_value=self.value();rollback_section=self.sh('uci -q get podkop.mkr_router_main || :');rollback_type=self.sh('uci -q get podkop.main.user_subnet_list_type || :');rollback_domains=self.sh('uci -q get podkop.mkr_router_main.user_domains_text || :');rollback_subnets=self.sh('uci -q get podkop.main.user_subnets_text || :');rollback_remote=self.sh('uci -q get podkop.mkr_router_main.remote_subnet_lists || :');assert rollback_value==OLD and rollback_section=='section' and rollback_type=='text' and rollback_domains=='ru xn--p1ai' and rollback_remote=='https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-ru.srs' and '0.0.0.0/0' in rollback_subnets,(rollback_value,rollback_section,rollback_type,rollback_domains,rollback_subnets,rollback_remote,self.status())
  self.source(NEW);self.cli('configure',{'mode':'auto','routing_mode':'all','interval':900,'section':'main','endpoint':'https://site.example/api/router-agent.php','token':''});self.cli('check');assert self.value()==NEW and self.sh('uci -q get podkop.mkr_router_main || :')=='' and '0.0.0.0/0' in self.sh('uci -q get podkop.main.fully_routed_ips')
  self.reset('0.7.22');self.sh('uci add_list podkop.main.community_lists=russia_outside;uci commit podkop');self.cli('configure',{'mode':'auto','routing_mode':'blocked','interval':900,'section':'main','endpoint':'https://site.example/api/router-agent.php','token':''});self.cli('check',ok=False);conflict_status=self.status();conflict_value=self.value();assert conflict_status['state']=='manual' and conflict_status['reason']=='routing_conflict' and conflict_value==OLD,(conflict_status,conflict_value)
  # Real 0.7 schema: separate settings, connection_type, and configurable health-check path.
  self.reset('0.7.22',mode='notify');before=self.sh('uci export podkop');assert self.sh('uci -q get podkop.main.mode || :')==''
  self.sh('touch /tmp/podkop-on;uci set podkop.settings.config_path=/etc/sing-box/podkop-custom.json;uci commit podkop');self.put('/etc/sing-box/podkop-custom.json','{}')
  before=self.sh('uci export podkop');self.cli('check');assert self.value()==OLD and self.status()['state']=='available'
  self.cli('apply');assert self.value()==NEW and self.status()['state']=='applied'
  assert self.sh('uci export podkop')==before.replace(OLD,NEW)
  assert self.sh('cat /tmp/last-check-path')=='/etc/sing-box/podkop-custom.json'
  self.reset('0.7.22');self.sh('uci set podkop.main.connection_type=vpn;uci set podkop.main.mode=proxy;uci commit podkop');self.cli('check');assert self.value()==OLD and self.status()['state']=='manual'
  for kind in ['selector','urltest','outbound']:
   self.reset('0.7.22');self.sh('uci set podkop.main.proxy_config_type='+kind+';uci commit podkop');self.cli('check');assert self.value()==OLD and self.status()['state']=='manual'
  self.reset('0.7.22',extra="config section 'second'\n option connection_type 'proxy'\n option proxy_config_type 'url'\n option proxy_string 'vless://other@other.example:443#Other'\n")
  self.cli('check');assert self.value()==OLD and self.status()['state']=='manual'
  self.cli('configure',{'mode':'auto','routing_mode':'blocked','interval':900,'section':'main','endpoint':'https://site.example/api/router-agent.php','token':''});self.cli('check');assert self.value()==NEW and 'other.example' in self.sh('uci get podkop.second.proxy_string')
  # Firmware may have no usable package query. The compiled version is data, not shell.
  self.reset('0.7.22');self.sh('touch /tmp/no-package-version');self.put('/usr/lib/podkop/constants.sh','PODKOP_VERSION="0.7.22"\n');self.cli('check');assert self.status()['state']=='applied' and self.status()['podkop_version']=='0.7.22'
  self.reset('0.7.22');self.sh('touch /tmp/no-package-version');self.put('/usr/lib/podkop/constants.sh','PODKOP_VERSION="$(touch /tmp/version-injected)"\n');self.cli('check');assert self.value()==OLD and self.status()['state']=='manual';assert self.sh('test ! -f /tmp/version-injected && echo yes')=='yes'
  self.reset('0.7.22');self.sh('touch /tmp/podkop-on /tmp/restart-fail');self.cli('check',ok=False);assert self.value()==OLD and self.status()['reason']=='restart_failed'
  self.reset();self.sh('touch /tmp/podkop-on');self.cli('check');assert self.sh('wc -l < /tmp/restarts')=='1';self.cli('check');assert self.sh('wc -l < /tmp/restarts')=='1'
  for marker in ['podkop-starting','without-routing','routing-only']:
   self.reset();self.sh('touch /tmp/podkop-on /tmp/'+marker if marker!='routing-only' else 'touch /tmp/routing-only');self.cli('check');assert self.value()==OLD and self.status()['reason']=='runtime_state'
  self.reset(mode='notify');self.cli('check');assert self.value()==OLD and self.status()['state']=='available';self.cli('apply');assert self.value()==NEW
  self.reset('0.8.0');self.cli('check');assert self.value()==OLD and self.status()['state']=='manual'
  self.reset(extra="config extra 'other'\n option mode 'proxy'\n option proxy_config_type 'url'\n option proxy_string 'vless://other@other.example:443#Other'\n");self.cli('check');assert self.value()==OLD and self.status()['state']=='manual'
  # Explicit section works without touching another section.
  self.cli('configure',{'mode':'auto','routing_mode':'blocked','interval':900,'section':'main','endpoint':'https://site.example/api/router-agent.php','token':''});self.cli('check');assert self.value()==NEW and 'other.example' in self.sh('uci -q get podkop.other.proxy_string')
  self.reset();self.sh("uci set podkop.main.proxy_config_type=urltest;uci commit podkop");self.cli('check');assert self.value()==OLD and self.status()['state']=='manual'
  self.reset();self.put('/tmp/multiline',OLD+'\n// vless://backup@backup.example:443#Backup');self.sh('uci set "podkop.main.proxy_string=$(cat /tmp/multiline)";uci commit podkop');self.cli('check');assert self.status()['state']=='manual' and 'backup' in self.value()
  # Download a base64 subscription, pin the existing node while the host changes.
  self.reset();self.source('https://subscription.example/sub/token','subscription');self.put('/tmp/subscription.txt',__import__('base64').b64encode((NEW+'\n'+OLD.replace('11111111','22222222').replace('#Home','#Other')).encode()));self.cli('check');assert self.value()==NEW;assert self.sh('test ! -f /tmp/sub-auth-leak && echo yes')=='yes'
  self.reset();self.source('https://subscription.example/sub/token','subscription');self.put('/tmp/subscription.txt',NEW+'\n'+NEW.replace('new.example','ambiguous.example'));self.cli('check');assert self.status()['reason']=='choose_node' and self.value()==OLD
  # A local edit after pairing must never be overwritten automatically.
  self.reset(mode='notify');self.cli('check');self.sh('uci set podkop.main.proxy_string="vless://local@local.example:443#Local";uci commit podkop');self.cli('apply');assert 'local.example' in self.value() and self.status()['reason']=='local_changed'
  self.reset();self.sh('uci set podkop.main.dns_server=8.8.8.8');self.cli('check');assert self.value()==OLD and self.status()['reason']=='unsaved_changes';assert '8.8.8.8' in self.sh('uci changes podkop')
  # Failed service restart rolls back only our field; simultaneous DNS edit survives.
  self.reset();self.sh('touch /tmp/podkop-on /tmp/restart-fail /tmp/dns-edit');self.cli('check',ok=False);assert self.value()==OLD and self.status()['reason']=='restart_failed';assert self.sh('uci get podkop.main.dns_server')=='9.9.9.9'
  self.reset();self.sh('touch /tmp/podkop-on /tmp/check-fail');self.cli('check',ok=False);assert self.value()==OLD and self.status()['reason']=='check_failed'
  # Interrupted update recovery also preserves unrelated settings.
  self.reset();self.put('/etc/mkr-router/previous-value',OLD);self.put('/etc/mkr-router/journal.json',json.dumps({'section':'main','revision':hashlib.sha256(NEW.encode()).hexdigest(),'previous':hashlib.sha256(OLD.encode()).hexdigest(),'running':'no'}));self.sh('uci set podkop.main.proxy_string="'+NEW+'";uci set podkop.main.dns_server=8.8.4.4;uci commit podkop');self.cli('recover');assert self.value()==OLD and self.sh('uci get podkop.main.dns_server')=='8.8.4.4'
  self.reset();self.put('/tmp/http-code','401');self.cli('check',ok=False);assert self.value()==OLD and self.status()['state']=='auth'
  self.put('/tmp/http-code','410');self.cli('check',ok=False);assert self.value()==OLD and self.status()['state']=='unavailable'
  self.reset();self.put('/tmp/curl-fail','');self.cli('check',ok=False);assert self.value()==OLD and self.status()['reason']=='network'
  self.reset();evil=NEW+"'$(touch${IFS}/tmp/INJECTED)";self.source(evil);self.cli('check');assert self.value()==evil;assert self.sh('test ! -f /tmp/INJECTED && test ! -f /tmp/argv-leak && echo yes')=='yes'
  self.reset();self.cli('configure',{'mode':'auto','routing_mode':'blocked','interval':900,'section':'','endpoint':'https://site.example/api/router-agent.php','token':'','injected.option':'bad'});assert 'injected' not in self.sh('uci show mkr_router')
  self.cli('disable');assert self.sh('uci -q get mkr_router.main.token || :')=='' and self.status()['state']=='unconfigured';assert self.value()==OLD
  assert self.sh('ls -l /etc/config/mkr_router | cut -c1-10')=='-rw-------'
  print('PASS',self.version,': real UCI, Podkop 0.3–0.7 fixtures including upstream 0.7.22, version detection/fallback, settings and connection_type, auto/notify, unknown/complex fallback, subscription selection, token isolation, rollback/recovery, conflicts, auth/network failures, injection and permissions.',flush=True)
if __name__=='__main__':
 import sys
 for v in (sys.argv[1:] or ['24.10.4','25.12.5']):Router(v).tests()

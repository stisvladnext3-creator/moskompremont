#!/usr/bin/env python3
from pathlib import Path
import argparse,json,os,subprocess,time
from platforms import docker,D,ENV

def install(dist,version):
 for platform in ['24.10.4','25.12.5']:
  name='mkr-test-'+platform
  for package in ['mkr-router-updater','luci-app-mkr-router']:
   f=dist/(f'{package}_{version}-1_all.ipk' if platform.startswith('24') else f'{package}-{version}-r1.apk')
   docker('cp',str(f),name+':/tmp/'+f.name)
   command=['opkg','install','/tmp/'+f.name] if platform.startswith('24') else ['apk','add','--allow-untrusted','/tmp/'+f.name]
   p=docker('exec',name,*command,capture_output=True,text=True)
   assert 'Malformed' not in p.stderr and 'Collected errors' not in p.stderr,p.stderr
   # Repeat installation must not destroy the saved credential/configuration.
  docker('exec',name,'sh','-c',"uci set mkr_router.main.token='fixture-preserved';uci commit mkr_router")
  core=dist/(f'mkr-router-updater_{version}-1_all.ipk' if platform.startswith('24') else f'mkr-router-updater-{version}-r1.apk')
  command=['opkg','install','--force-reinstall','/tmp/'+core.name] if platform.startswith('24') else ['apk','fix','--allow-untrusted','--reinstall','mkr-router-updater']
  # apk needs its local file in the repository resolver for a reinstall.
  if not platform.startswith('24'):command=['apk','add','--allow-untrusted','/tmp/'+core.name]
  docker('exec',name,*command,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
  p=docker('exec',name,'uci','get','mkr_router.main.token',capture_output=True,text=True);assert p.stdout.strip()=='fixture-preserved'
  docker('exec',name,'sh','-c','mkdir -p /var/run; ubusd >/tmp/ubusd.log 2>&1 &')
  time.sleep(.2);docker('exec',name,'sh','-c','rpcd -s /var/run/ubus/ubus.sock >/tmp/rpcd.log 2>&1 &')
  time.sleep(.5)
  p=docker('exec',name,'ubus','call','mkr-router','status','{}',capture_output=True,text=True)
  status=json.loads(p.stdout);assert status['settings']['has_token'] and 'token' not in status['settings']
  assert status['version']==version
  for path in ['/usr/share/luci/menu.d/luci-app-mkr-router.json','/usr/share/rpcd/acl.d/luci-app-mkr-router.json']:
   docker('exec',name,'test','-f',path)
  print('PASS native package installation/repeat installation + real rpcd/ubus:',platform,flush=True)
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--dist',type=Path,required=True);p.add_argument('--version',default='0.1.0');a=p.parse_args();install(a.dist,a.version)

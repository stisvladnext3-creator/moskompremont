#!/usr/bin/env python3
from pathlib import Path
import argparse,json,os,subprocess,time,io,tarfile
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
  # Query the real installed package manager through a name-only fixture. This
  # catches apk 3's changed info output, instead of mocking the version format.
  real=docker('exec',name,'sh','-c','command -v '+('opkg' if platform.startswith('24') else 'apk'),capture_output=True,text=True).stdout.strip()
  if platform.startswith('24'):
   wrapper='#!/bin/sh\nexec '+real+' status mkr-router-updater\n';manager='opkg'
  else:
   wrapper='#!/bin/sh\n'+real+' "$1" "$2" "$3" mkr-router-updater | sed "s/^mkr-router-updater-/podkop-/"\n';manager='apk'
  payload=io.BytesIO()
  with tarfile.open(fileobj=payload,mode='w') as archive:
   info=tarfile.TarInfo('tmp/version-query/'+manager);data=wrapper.encode();info.size=len(data);info.mode=0o755;archive.addfile(info,io.BytesIO(data))
   if manager=='apk':
    info=tarfile.TarInfo('tmp/version-query/opkg');data=b'#!/bin/sh\nexit 1\n';info.size=len(data);info.mode=0o755;archive.addfile(info,io.BytesIO(data))
  subprocess.run(D+['exec','-i',name,'tar','-xf','-','-C','/'],input=payload.getvalue(),env=ENV,check=True,capture_output=True)
  probe=docker('exec','-e','PATH=/tmp/version-query:/usr/sbin:/usr/bin:/sbin:/bin',name,'/usr/bin/mkr-router','status',capture_output=True,text=True)
  assert json.loads(probe.stdout)['podkop_version']==version,probe.stdout
  for path in ['/usr/share/luci/menu.d/luci-app-mkr-router.json','/usr/share/rpcd/acl.d/luci-app-mkr-router.json']:
   docker('exec',name,'test','-f',path)
  print('PASS native package installation/repeat installation + real rpcd/ubus:',platform,flush=True)
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--dist',type=Path,required=True);p.add_argument('--version',default='0.1.4');a=p.parse_args();install(a.dist,a.version)

#!/usr/bin/env python3
"""Fetch checksum-pinned official rootfs; prepare native opkg/apk + LuCI containers."""
from pathlib import Path
import argparse, hashlib, json, os, subprocess, urllib.request
ROOT=Path(__file__).resolve().parent
D=['docker'];ENV=os.environ.copy()
for key in ['DOCKER_HOST','DOCKER_CONTEXT','DOCKER_TLS','DOCKER_TLS_VERIFY','DOCKER_CERT_PATH']:ENV.pop(key,None)
if os.environ.get('MKR_DOCKER_SOCKET'):D+=['--host='+os.environ['MKR_DOCKER_SOCKET']]
def docker(*args,**kwargs):return subprocess.run(D+list(args),env=ENV,check=True,**kwargs)
def prepare(work):
 work.mkdir(parents=True,exist_ok=True)
 for platform in json.loads((ROOT/'platforms.json').read_text()):
  version=platform['version'];name=Path(platform['url']).name;p=work/name
  if not p.is_file():p.write_bytes(urllib.request.urlopen(platform['url'],timeout=90).read())
  assert hashlib.sha256(p.read_bytes()).hexdigest()==platform['sha256'],name
  recipe=work/('Dockerfile-'+version)
  recipe.write_text('FROM scratch\nADD '+name+' /\nENV PATH=/usr/sbin:/usr/bin:/sbin:/bin\nRUN mkdir -p /var/lock /tmp && chmod 1777 /tmp\nRUN if command -v opkg >/dev/null; then opkg update && opkg install curl jq flock ca-bundle coreutils-base64 luci-base rpcd-mod-file; else apk update && apk add curl jq flock ca-bundle coreutils-base64 luci-base rpcd-mod-file; fi\nCMD ["/bin/sh"]\n')
  docker('build','-t','mkr-openwrt:'+version,'-f',str(recipe),str(work),stdout=subprocess.DEVNULL)
  docker('run','-d','--name','mkr-test-'+version,'--entrypoint','/bin/sleep','mkr-openwrt:'+version,'86400',stdout=subprocess.DEVNULL)
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--work',type=Path,required=True);prepare(p.parse_args().work)

#!/usr/bin/env python3
"""Reproducible IPK and APK-v3 (all) bundles. No OpenWrt toolchain required."""
from pathlib import Path
import argparse, shlex, gzip, hashlib, io, json, os, re, shutil, subprocess, tarfile, tempfile, urllib.request, zipfile
ROOT=Path(__file__).resolve().parent
APK_URL='https://dl-cdn.alpinelinux.org/alpine/edge/main/x86_64/apk-tools-static-3.0.9-r0.apk'
APK_SHA='677dddb435ca7d621ce9f8a1a404a4cedc631651fd025893c548c34a4ceecb19'
EPOCH=1794614400
POST='''#!/bin/sh
[ -n "$IPKG_INSTROOT" ] && exit 0
chmod 600 /etc/config/mkr_router
mkdir -p /etc/mkr-router
chmod 700 /etc/mkr-router
/etc/init.d/mkr_router enable >/dev/null 2>&1 || :
/etc/init.d/mkr_router restart >/dev/null 2>&1 || :
exit 0
'''
PRE='''#!/bin/sh
[ -n "$IPKG_INSTROOT" ] && exit 0
[ "$1" = upgrade ] && exit 0
uci -q set mkr_router.main.enabled=0
uci -q commit mkr_router
/etc/init.d/mkr_router stop >/dev/null 2>&1 || :
/etc/init.d/mkr_router disable >/dev/null 2>&1 || :
exit 0
'''
LUCI_POST='''#!/bin/sh
[ -n "$IPKG_INSTROOT" ] && exit 0
rm -f /tmp/luci-indexcache
/etc/init.d/rpcd restart >/dev/null 2>&1 || :
exit 0
'''
def tgz(files):
 raw=io.BytesIO()
 with tarfile.open(fileobj=raw,mode='w',format=tarfile.USTAR_FORMAT) as t:
  directories=set()
  for name,_,_ in files:
   for parent in Path(name).parents:
    if str(parent)!='.':directories.add(parent.as_posix())
  for name in sorted(directories):
   info=tarfile.TarInfo('./'+name+'/');info.type=tarfile.DIRTYPE;info.mode=0o755;info.uid=info.gid=0;info.mtime=EPOCH;t.addfile(info)
  for name,data,mode in sorted(files):
   info=tarfile.TarInfo('./'+name);info.size=len(data);info.mode=mode;info.uid=info.gid=0;info.mtime=EPOCH;t.addfile(info,io.BytesIO(data))
 out=io.BytesIO()
 with gzip.GzipFile(fileobj=out,mode='wb',mtime=0,filename='') as g:g.write(raw.getvalue())
 return out.getvalue()
def build(version,out,apk):
 assert re.fullmatch(r'0\.1\.\d+',version)
 out.mkdir(parents=True,exist_ok=True)
 with tempfile.TemporaryDirectory() as temp:
  temp=Path(temp)
  if not apk:
   download=urllib.request.urlopen(APK_URL,timeout=60).read();assert hashlib.sha256(download).hexdigest()==APK_SHA
   # Only extract the pinned static build tool, never execute archive hooks.
   with tarfile.open(fileobj=io.BytesIO(download),mode='r:gz') as t:binary=t.extractfile('sbin/apk.static').read()
   apk=temp/'apk';apk.write_bytes(binary);apk.chmod(0o755)
  specs=[('mkr-router-updater','files','curl, jq, uci, ca-bundle, flock, coreutils-base64','Updater for a single MosKomRemont Podkop connection',POST,PRE),('luci-app-mkr-router','luci','luci-base, mkr-router-updater','LuCI interface for the MosKomRemont updater',LUCI_POST,None)]
  for name,source,deps,desc,post,pre in specs:
   stage=temp/name;stage.mkdir()
   if source=='files':shutil.copytree(ROOT/'files',stage,dirs_exist_ok=True)
   else:
    shutil.copytree(ROOT/'luci/root',stage,dirs_exist_ok=True)
    shutil.copytree(ROOT/'luci/htdocs',stage/'www',dirs_exist_ok=True)
   files=[]
   stage.chmod(0o755)
   for folder in stage.rglob('*'):
    if folder.is_dir():folder.chmod(0o755)
   for p in sorted(stage.rglob('*')):
    if not p.is_file():continue
    rel=p.relative_to(stage).as_posix();data=p.read_bytes()
    if rel=='usr/bin/mkr-router':data=data.replace(b"MKR_VERSION='0.1.0'",f"MKR_VERSION='{version}'".encode());p.write_bytes(data)
    mode=0o600 if rel=='etc/config/mkr_router' else 0o755 if rel.startswith(('usr/bin/','usr/libexec/','etc/init.d/')) else 0o644
    p.chmod(mode);os.utime(p,(EPOCH,EPOCH));files.append((rel,data,mode))
   control=f'Package: {name}\nVersion: {version}-1\nArchitecture: all\nMaintainer: MosKomRemont\nSection: net\nPriority: optional\nLicense: GPL-2.0-or-later\nDepends: {deps}\nDescription: {desc}\nInstalled-Size: {sum(len(x[1]) for x in files)}\n'
   controls=[('control',control.encode(),0o644),('postinst',post.encode(),0o755)]
   if pre:controls.append(('prerm',pre.encode(),0o755));controls.append(('conffiles',b'/etc/config/mkr_router\n',0o644))
   (out/f'{name}_{version}-1_all.ipk').write_bytes(tgz([('debian-binary',b'2.0\n',0o644),('control.tar.gz',tgz(controls),0o644),('data.tar.gz',tgz(files),0o644)]))
   cmd=[str(apk),'mkpkg','--files',str(stage),'--info','name:'+name,'--info','version:'+version+'-r1','--info','arch:noarch','--info','description:'+desc,'--info','license:GPL-2.0-or-later','--info','origin:mkr-router-updater','--info','maintainer:MosKomRemont','--info','depends:'+deps.replace(',',''),'--output',str(out/f'{name}-{version}-r1.apk')]
   for hook,content in [('post-install',post),('post-upgrade',post),('pre-deinstall',pre)]:
    if content:
     f=temp/(name+'-'+hook);f.write_text(content);f.chmod(0o755);cmd+=['--script',hook+':'+str(f)]
   # mkpkg reads owners directly; run in a root container from a tar stream
   # with uid/gid 0, including when the build host is an unprivileged user.
   payload=io.BytesIO()
   with tarfile.open(fileobj=payload,mode='w') as archive:
    def add(path,name):
     data=path.read_bytes();info=tarfile.TarInfo(name);info.size=len(data);info.mode=path.stat().st_mode&0o777;info.uid=info.gid=0;info.uname=info.gname='root';info.mtime=EPOCH;archive.addfile(info,io.BytesIO(data))
    add(Path(apk),'apk');
    for path in sorted(stage.rglob('*')):
     if path.is_file():add(path,'root/'+path.relative_to(stage).as_posix())
    remap={str(apk):'/build/apk',str(stage):'/build/root',cmd[cmd.index('--output')+1]:'/build/result.apk'}
    for path in temp.glob(name+'-*'):
     if path.is_file():add(path,'scripts/'+path.name);remap[str(path)]='/build/scripts/'+path.name
   mapped=[]
   for arg in cmd:
    if arg in remap:arg=remap[arg]
    elif ':' in arg and arg.split(':',1)[1] in remap:
     prefix,path=arg.split(':',1);arg=prefix+':'+remap[path]
    mapped.append(arg)
   docker_env=os.environ.copy()
   for key in ['DOCKER_HOST','DOCKER_CONTEXT','DOCKER_TLS','DOCKER_TLS_VERIFY','DOCKER_CERT_PATH']:docker_env.pop(key,None)
   docker=['docker']
   if os.environ.get('MKR_DOCKER_SOCKET'):docker+=['--host='+os.environ['MKR_DOCKER_SOCKET']]
   command='mkdir -p /build && cd /build && tar xf - && chmod 755 /build /build/root && '+shlex.join(mapped)+' && cat /build/result.apk'
   result=subprocess.run(docker+['run','--rm','-i','--network=none',os.environ.get('MKR_APK_IMAGE','mkr-openwrt:25.12.5'),'sh','-c',command],input=payload.getvalue(),capture_output=True,env=docker_env)
   if result.returncode:raise RuntimeError(result.stderr.decode())
   (out/f'{name}-{version}-r1.apk').write_bytes(result.stdout)
  for p in out.glob('*.ipk'):assert p.read_bytes().startswith(b'\x1f\x8b')
  for p in out.glob('*.apk'):assert p.read_bytes().startswith(b'ADB'),p
  for fn in ['README.md','INSTALL-OPENWRT.md','LICENSE']:
   if (ROOT/fn).is_file():shutil.copyfile(ROOT/fn,out/fn)
  (out/'BUILD.json').write_text(json.dumps({'version':version,'source_date_epoch':EPOCH,'apk_builder_url':APK_URL,'apk_builder_sha256':APK_SHA},indent=2)+'\n')
  assets=sorted(p for p in out.iterdir() if p.is_file() and p.name!='SHA256SUMS.txt' and p.suffix!='.zip')
  (out/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in assets))
  with zipfile.ZipFile(out/f'moskom-openwrt-{version}.zip','w',zipfile.ZIP_DEFLATED) as z:
   for p in sorted(out.iterdir()):
    if p.is_file() and p.suffix!='.zip':z.write(p,p.name)
 return out
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--version',default='0.1.0');p.add_argument('--out',type=Path,required=True);p.add_argument('--apk-tool',type=Path);a=p.parse_args();build(a.version,a.out.resolve(),a.apk_tool)

#!/usr/bin/env python3
"""Reproducible Entware IPK (scripts only) and a narrowly scoped site update."""
from pathlib import Path
import argparse, gzip, hashlib, io, json, re, shutil, tarfile, zipfile
ROOT=Path(__file__).resolve().parent
EPOCH=1795392000

def tgz(files):
 raw=io.BytesIO()
 with tarfile.open(fileobj=raw,mode='w',format=tarfile.USTAR_FORMAT) as tar:
  dirs={str(p) for name,_,_ in files for p in Path(name).parents if str(p)!='.'}
  for name in sorted(dirs):
   i=tarfile.TarInfo('./'+name+'/');i.type=tarfile.DIRTYPE;i.mode=0o755;i.uid=i.gid=0;i.mtime=EPOCH;tar.addfile(i)
  for name,data,mode in sorted(files):
   i=tarfile.TarInfo('./'+name);i.size=len(data);i.mode=mode;i.uid=i.gid=0;i.mtime=EPOCH;tar.addfile(i,io.BytesIO(data))
 out=io.BytesIO()
 with gzip.GzipFile(fileobj=out,mode='wb',mtime=0,filename='') as gz:gz.write(raw.getvalue())
 return out.getvalue()

def zip_files(path,files):
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as z:
  for name,data in sorted(files):
   i=zipfile.ZipInfo(name,(2026,11,23,0,0,0));i.external_attr=0o100644<<16;i.compress_type=zipfile.ZIP_DEFLATED;z.writestr(i,data)

def build(out,version):
 if not re.fullmatch(r'0\.1\.\d+',version):raise ValueError('version')
 out.mkdir(parents=True,exist_ok=True)
 data=[]
 for p in sorted((ROOT/'files').rglob('*')):
  if p.is_file():
   name=p.relative_to(ROOT/'files').as_posix();content=p.read_bytes()
   if name=='opt/bin/mkr-xkeen':content=re.sub(rb'(?m)^VERSION=[0-9.]+$',('VERSION='+version).encode(),content)
   mode=0o755 if name.startswith(('opt/bin/','opt/etc/init.d/')) else 0o644
   data.append((name,content,mode))
 control=f'''Package: mkr-xkeen-updater
Version: {version}-1
Architecture: all
Maintainer: MosKomRemont
Section: net
Priority: optional
License: GPL-2.0-or-later
Depends: curl, jq, ca-bundle, flock, coreutils-timeout, coreutils-sha256sum, cron
Description: Update a single XKeen Xray VLESS outbound from MosKomRemont
Installed-Size: {sum(len(d) for _,d,_ in data)}
'''
 post=b'''#!/bin/sh
[ -n "$IPKG_INSTROOT" ] && exit 0
mkdir -p /opt/etc/mkr-xkeen /opt/var/lib/mkr-xkeen
chmod 700 /opt/etc/mkr-xkeen /opt/var/lib/mkr-xkeen
[ ! -f /opt/etc/mkr-xkeen/config.json ] || chmod 600 /opt/etc/mkr-xkeen/config.json
exit 0
'''
 pre=b'''#!/bin/sh
[ -n "$IPKG_INSTROOT" ] && exit 0
[ "$1" = upgrade ] && exit 0
/opt/bin/mkr-xkeen unschedule >/dev/null 2>&1 || :
exit 0
'''
 controls=[('control',control.encode(),0o644),('postinst',post,0o755),('prerm',pre,0o755)]
 package=f'mkr-xkeen-updater_{version}-1_all.ipk'
 (out/package).write_bytes(tgz([('debian-binary',b'2.0\n',0o644),('control.tar.gz',tgz(controls),0o644),('data.tar.gz',tgz(data),0o644)]))
 digest=hashlib.sha256((out/package).read_bytes()).hexdigest()
 install=(ROOT/'install.template.sh').read_text().replace('@VERSION@',version).replace('@SHA256@',digest)
 (out/'install.sh').write_text(install)
 for name in ['README.md','INSTALL-KEENETIC.md','UPDATE-KEENETIC.md','VALIDATION.md','LICENSE','SOURCES.json']:
  if (ROOT/name).is_file():shutil.copyfile(ROOT/name,out/name)
 (out/'BUILD.json').write_text(json.dumps({'version':version,'architecture':'all','format':'Entware IPK (tar.gz)','sha256':digest,'source_date_epoch':EPOCH},indent=2)+'\n')
 assets=[p for p in sorted(out.iterdir()) if p.is_file() and p.name!='SHA256SUMS.txt' and p.suffix!='.zip']
 (out/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in assets))
 zip_files(out/f'moskom-keenetic-{version}.zip',[(p.name,p.read_bytes()) for p in sorted(out.iterdir()) if p.is_file() and p.suffix!='.zip'])
 # No config.php, user data, website-wide files, or application download versions.
 names=['account.html','assets/account.js','api/router-agent.php','private/router-devices.php','private/xray-outbound.php','private/migrations/011-router-devices.sql']
 site=[(name,(ROOT.parent/'module'/name).read_bytes()) for name in names]
 site += [('downloads/keenetic/install.sh',install.encode()),(f'downloads/keenetic/{version}/{package}',(out/package).read_bytes()),(f'downloads/keenetic/{version}/SHA256SUMS.txt',(out/'SHA256SUMS.txt').read_bytes()),('INSTALL-KEENETIC.md',(ROOT/'INSTALL-KEENETIC.md').read_bytes())]
 zip_files(out/f'moskom-keenetic-site-update-{version}.zip',site)
 repair=[(name,content) for name,content in site if name.startswith('downloads/')]
 repair.append(('UPDATE-KEENETIC.md',(ROOT/'UPDATE-KEENETIC.md').read_bytes()))
 zip_files(out/f'moskom-keenetic-repair-{version}.zip',repair)
 return out

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--out',type=Path,required=True);p.add_argument('--version',default='0.1.1');a=p.parse_args();build(a.out,a.version)

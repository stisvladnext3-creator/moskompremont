#!/usr/bin/env python3
"""Reproducibility/contents checks and optional offline Entware opkg install.
The offline opkg fixture registers dependency metadata only; it does not
pretend to exercise those installed tools or a physical router.
"""
from pathlib import Path
import hashlib, io, json, os, re, subprocess, sys, tarfile, tempfile, zipfile
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from build import build
with tempfile.TemporaryDirectory(prefix='mkr-xkeen-package-') as work:
 work=Path(work);a=build(work/'a','0.1.1');b=build(work/'b','0.1.1')
 for p in a.iterdir():assert p.read_bytes()==(b/p.name).read_bytes(),p.name
 ipk=a/'mkr-xkeen-updater_0.1.1-1_all.ipk'
 with tarfile.open(fileobj=io.BytesIO(ipk.read_bytes()),mode='r:gz') as tar:
  assert tar.extractfile('./debian-binary').read()==b'2.0\n'
  control=tarfile.open(fileobj=io.BytesIO(tar.extractfile('./control.tar.gz').read()),mode='r:gz')
  c=control.extractfile('./control').read().decode();assert 'Architecture: all\n' in c
  assert 'coreutils-timeout' in c and 'coreutils-sha256sum' in c
  data=tarfile.open(fileobj=io.BytesIO(tar.extractfile('./data.tar.gz').read()),mode='r:gz')
  names=[m.name for m in data if m.isfile()]
  assert sorted(names)==sorted(['./opt/bin/mkr-xkeen','./opt/lib/mkr-xkeen/jsonc.awk','./opt/etc/init.d/S99mkr-xkeen'])
  for member in data:
   assert member.uid==member.gid==0
   if member.name.endswith('/mkr-xkeen'):assert member.mode==0o755
 digest=hashlib.sha256(ipk.read_bytes()).hexdigest();installer=(a/'install.sh').read_text();assert "SHA256='"+digest+"'" in installer
 assert '--proto' in installer and '--force-' not in installer and '--no-check-certificate' not in installer
 subprocess.run(['sh','-n',str(a/'install.sh')],check=True)
 with zipfile.ZipFile(a/'moskom-keenetic-site-update-0.1.1.zip') as z:
  names=z.namelist();assert 'private/config.php' not in names and 'download.html' not in names and 'private/schema.sql' not in names
  assert 'private/xray-outbound.php' in names and 'downloads/keenetic/install.sh' in names
  assert all(not n.startswith('/') and '..' not in Path(n).parts for n in names)
 with zipfile.ZipFile(a/'moskom-keenetic-repair-0.1.1.zip') as z:
  assert sorted(z.namelist())==sorted(['downloads/keenetic/install.sh','downloads/keenetic/0.1.1/mkr-xkeen-updater_0.1.1-1_all.ipk','downloads/keenetic/0.1.1/SHA256SUMS.txt','UPDATE-KEENETIC.md'])
  assert z.read('downloads/keenetic/install.sh')==(a/'install.sh').read_bytes()
 opkg=os.environ.get('MKR_ENTWARE_OPKG')
 if opkg:
  offline=work/'offline';(offline/'opt/lib/opkg/info').mkdir(parents=True);(offline/'opt/tmp').mkdir(parents=True);(offline/'opt/var/opkg-lists').mkdir(parents=True)
  deps=['curl','jq','ca-bundle','flock','coreutils-timeout','coreutils-sha256sum','cron']
  (offline/'opt/lib/opkg/status').write_text('\n\n'.join('Package: '+d+'\nVersion: 99.0\nStatus: install ok installed\nArchitecture: all' for d in deps)+'\n\n')
  for d in deps:(offline/'opt/lib/opkg/info'/(d+'.list')).write_text('')
  config=work/'opkg.conf';config.write_text('dest root /\nlists_dir ext /opt/var/opkg-lists\narch all 100\n')
  cmd=[opkg,'-o',str(offline),'-t',str(offline/'opt/tmp'),'-f',str(config)]
  # Exercise the real package-manager upgrade path from the shipped 0.1.0.
  with zipfile.ZipFile(Path(__file__).resolve().parents[2]/'downloads/moskom-keenetic-0.1.0.zip') as old:
   old_ipk=work/'mkr-xkeen-updater_0.1.0-1_all.ipk';old_ipk.write_bytes(old.read(old_ipk.name))
  r=subprocess.run(cmd+['install',str(old_ipk)],capture_output=True,text=True);assert r.returncode==0,r.stdout+r.stderr
  saved=offline/'opt/etc/mkr-xkeen/config.json';saved.parent.mkdir(parents=True);saved.write_text('{"mode":"notify","tag":"existing-proxy","token":"fixture-keep-private"}')
  backup=offline/'opt/var/lib/mkr-xkeen/previous.conf';backup.parent.mkdir(parents=True);backup.write_bytes(b'fixture original key config')
  saved_before=saved.read_bytes();backup_before=backup.read_bytes()
  r=subprocess.run(cmd+['install',str(ipk)],capture_output=True,text=True);assert r.returncode==0,r.stdout+r.stderr
  assert 'Upgrading' in r.stdout and 'Version: 0.1.1-1' in (offline/'opt/lib/opkg/status').read_text()
  assert saved.read_bytes()==saved_before and backup.read_bytes()==backup_before
  assert (offline/'opt/bin/mkr-xkeen').is_file() and (offline/'opt/bin/mkr-xkeen').stat().st_mode&0o777==0o755
  assert 'Package: mkr-xkeen-updater' in (offline/'opt/lib/opkg/status').read_text()
  r=subprocess.run(cmd+['remove','mkr-xkeen-updater'],capture_output=True,text=True);assert r.returncode==0,r.stdout+r.stderr
  assert not (offline/'opt/bin/mkr-xkeen').exists()
  print('PASS: native Entware opkg offline 0.1.0 -> 0.1.1 upgrade preserving settings/backups, remove, Architecture all, without forcing dependencies, checksums or signatures.')
 print('PASS: reproducible IPK/ZIP bytes, root ownership/modes, exact payload, pinned HTTPS package checksum, narrow site update, shell syntax.')

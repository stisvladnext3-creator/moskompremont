#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,stat,zipfile
ROOT=Path(__file__).resolve().parents[1]
def package(out):
 out.mkdir(parents=True,exist_ok=True);target=out/'moskompremont-cabinet-v21-update.zip'
 with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as z:
  for p in sorted((ROOT/'module').rglob('*')):
   if not p.is_file():continue
   relative=p.relative_to(ROOT/'module').as_posix()
   assert relative not in ['private/config.php','assets/config.js','images/payment/qr.png'],relative
   assert not relative.endswith(('.pyc','.p12','.key','.pem')),relative
   if relative.endswith('.csv'):assert p.read_text(encoding='utf-8-sig').strip()=='name,url'
   info=zipfile.ZipInfo(relative);info.date_time=(2026,11,14,12,0,0);info.create_system=3;info.external_attr=(stat.S_IFREG|0o644)<<16;info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,p.read_bytes())
 with zipfile.ZipFile(target) as z:
  assert z.testzip() is None
  for relative in ['api/router-agent.php','private/router-devices.php','private/migrations/011-router-devices.sql','UPDATE-CABINET-V21.md','private/order-renewals.php']:
   assert z.read(relative)==(ROOT/'module'/relative).read_bytes()
  assert 'account.js?v=21' in z.read('account.html').decode()
 print(target.name,target.stat().st_size,hashlib.sha256(target.read_bytes()).hexdigest())
 return target
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--out',type=Path,required=True);package(p.parse_args().out)

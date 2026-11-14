#!/usr/bin/env python3
from pathlib import Path
import argparse,os,json,subprocess,plistlib,shutil,hashlib
p=argparse.ArgumentParser();p.add_argument('work',type=Path);a=p.parse_args();work=a.work.resolve();app=work/'MoskomVPN.app';payload=app/'Contents/MacOS';output=work/'smoke';output.mkdir();profile=work/'smoke-profile'
def contents():return {str(p.relative_to(app)):hashlib.sha256(p.read_bytes()).hexdigest() for p in app.rglob('*') if p.is_file()}
before=contents();env=os.environ.copy();env.update({'MOSKOM_SMOKE_TEST':'1','MOSKOM_SMOKE_HOME':str(profile),'MOSKOM_SMOKE_OUTPUT':str(output)})
# An installed application may be owned by another administrator. The normal
# UI/test profile must work without writing to its signed application bundle.
subprocess.run(['chmod','-R','a-w',str(app)],check=True)
with (work/'application.log').open('w') as log:
 proc=subprocess.run([str(payload/'MoskomVPN')],env=env,stdout=log,stderr=log,timeout=240)
if (output/'smoke-error.txt').exists():raise RuntimeError((output/'smoke-error.txt').read_text())
assert proc.returncode==0,(proc.returncode,(work/'application.log').read_text()[-5000:])
report=json.loads((output/'smoke.json').read_text());assert report['status']=='Disconnected' and report['vectorIcon'] and report['website']['SessionEncrypted']
assert before==contents(),'Application wrote inside its signed bundle'
subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
for file in sorted(output.glob('dns-*.json')):
 subprocess.run([str(payload/'bin/xray/xray'),'run','-test','-c',str(file)],env={**env,'XRAY_LOCATION_ASSET':str(payload/'bin')},check=True)
for file in sorted(output.glob('singbox-*.json')):
 subprocess.run([str(payload/'bin/sing_box/sing-box'),'check','-c',str(file)],check=True)
print('PASS: actual Mac launcher UI, cabinet/keychain/sync/isolation, DNS, Russia, updates, help, manual subscription removal and immutable app bundle.')

#!/usr/bin/env python3
from pathlib import Path
import argparse,json,shutil,subprocess,hashlib,zipfile,stat
OWN=Path(__file__).resolve().parents[1];REPO=OWN.parent
p=argparse.ArgumentParser();p.add_argument('work',type=Path);a=p.parse_args();work=a.work.resolve();info=json.loads((work/'build.json').read_text());version=info['version'];flavor=info['flavor'];rid=info['rid'];out=work/'release';out.mkdir(exist_ok=True)
stage=work/'dmg';stage.mkdir();shutil.copytree(work/'MoskomVPN.app',stage/'MoskomVPN.app',symlinks=True);(stage/'Applications').symlink_to('/Applications')
shutil.copyfile(OWN/'INSTALL-RU.md',stage/'КАК-УСТАНОВИТЬ.txt')
name=f'MoskomVPN-macOS-{flavor}-{rid}-{version}.dmg';dmg=out/name
subprocess.run(['hdiutil','create','-quiet','-fs','HFS+','-volname','МосКомпРемонт VPN '+version,'-srcfolder',str(stage),'-format','UDZO',str(dmg)],check=True)
subprocess.run(['hdiutil','verify',str(dmg)],check=True)
# Source: complete prepared v2rayN and pinned submodules plus both build overlays.
source=out/f'MoskomVPN-macOS-{flavor}-{rid}-{version}-source.zip'
with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED) as z:
 for base,prefix in [(work/'upstream','v2rayN-source'),(OWN,'macos-client'),(REPO/'desktop-client','desktop-client'),(REPO/'shared','shared'),(work/'sing-box-source','sing-box-source')]:
  for file in sorted(base.rglob('*')):
   rel=file.relative_to(base)
   if not file.is_file() or any(p in {'.git','bin','obj','__pycache__'} for p in rel.parts) or file.suffix=='.pyc':continue
   z.write(file,'MoskomVPN-source/'+prefix+'/'+rel.as_posix())
for src,dst in [(work/'native-audit.json',f'native-audit-{flavor}-{rid}.json'),(work/'smoke/smoke.json',f'validation-{flavor}-{rid}.json'),(work/'smoke/launcher.png',f'preview-{flavor}-{rid}.png')]:shutil.copyfile(src,out/dst)
for name in ['INSTALL-RU.md','VALIDATION.md']:shutil.copyfile(OWN/name,out/name)
(out/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(file.read_bytes()).hexdigest()+'  '+file.name+'\n' for file in sorted(out.iterdir()) if file.is_file() and file.name!='SHA256SUMS.txt'))
print('Packaged',dmg.name,dmg.stat().st_size)

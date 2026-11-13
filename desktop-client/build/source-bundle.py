from pathlib import Path
import sys,shutil
upstream,client,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
shutil.copytree(upstream,out/'v2rayN-modified',ignore=shutil.ignore_patterns('.git','bin','obj','.vs'),dirs_exist_ok=True)
shutil.copytree(client,out/'launcher-build',ignore=shutil.ignore_patterns('out','__pycache__'),dirs_exist_ok=True)
(out/'README.txt').write_text('Corresponding modified source for MosKomRemont VPN, based on v2rayN 7.25.6. GPL-3.0. Build instructions and pinned upstream/component sources are in launcher-build. No user subscriptions or configuration files are included.\n',encoding='utf-8')

from pathlib import Path
import sys,shutil
upstream,client,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
shutil.copytree(upstream,out/'v2rayN-modified',ignore=shutil.ignore_patterns('.git','bin','obj','.vs'),dirs_exist_ok=True)
shutil.copytree(client,out/'launcher-build',ignore=shutil.ignore_patterns('out','__pycache__'),dirs_exist_ok=True)
shutil.copytree(client.parent/'shared',out/'shared',dirs_exist_ok=True)
props=upstream/'v2rayN/Directory.Build.props'
text=props.read_text(encoding='utf-8') if props.exists() else ''
import re
match=re.search(r'<Version>([^<]+)</Version>',text)
upstream_version=match.group(1) if match else 'unknown'
(out/'README.txt').write_text(f'Corresponding modified source for MosKomRemont VPN, based on v2rayN {upstream_version}. GPL-3.0. Build instructions and pinned upstream/component sources are in launcher-build. No user subscriptions or configuration files are included.\n',encoding='utf-8')

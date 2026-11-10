#!/usr/bin/env python3
import getpass, grp, json, os, re, tempfile, urllib.parse
from pathlib import Path
path=Path('/etc/moskom-monitor/config.json')
previous=json.loads(path.read_text()) if path.exists() else {}
default=previous.get('endpoint','https://moskompremont.ru/api/monitor-agent.php')
endpoint=input('Адрес API сайта ['+default+']: ').strip() or default
url=urllib.parse.urlsplit(endpoint)
if url.scheme!='https' or not url.hostname or url.username or url.password or url.query or url.fragment:
 raise SystemExit('Нужен HTTPS-адрес api/monitor-agent.php без параметров.')
token=getpass.getpass('Токен из админки (ввод скрыт; Enter — сохранить прежний): ').strip() or previous.get('token','')
if not re.fullmatch('[0-9a-f]{64}',token):raise SystemExit('Токен должен содержать 64 символа. Скопируйте его целиком из админки.')
value={'endpoint':endpoint,'token':token,'xray':'/opt/moskom-monitor/bin/xray'}
with tempfile.NamedTemporaryFile('w',dir=str(path.parent),delete=False) as f:
 json.dump(value,f,indent=2);f.write('\n');temp=f.name
os.chmod(temp,0o640);os.chown(temp,0,grp.getgrnam('mkr-monitor').gr_gid);os.replace(temp,path)
print('Настройки сохранены в /etc/moskom-monitor/config.json')

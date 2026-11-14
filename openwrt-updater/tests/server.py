#!/usr/bin/env python3
"""Start/stop disposable PHP + MariaDB fixtures for backend.py and cabinet.cjs."""
from pathlib import Path
import argparse,os,shutil,subprocess,time,urllib.request
from platforms import docker,D,ENV
ROOT=Path(__file__).resolve().parents[2]
def start(work,image,php):
 app=work/'app';shutil.copytree(ROOT/'module',app,dirs_exist_ok=True)
 (app/'assets/config.js').write_text('window.SITE_CONFIG = {};\n')
 (app/'private/config.php').write_text("<?php return ['dsn'=>'mysql:host=mkr-v21-db;dbname=internet_test;charset=utf8mb4','db_user'=>'root','db_password'=>'','origin'=>'http://localhost:4201','secure_cookies'=>false,'admin_user_ids'=>[1],'admin_email'=>'admin@example.test','mail'=>['enabled'=>false]];")
 docker('network','create','mkr-v21-net',stdout=subprocess.DEVNULL)
 docker('run','-d','--name','mkr-v21-db','--network','mkr-v21-net','--tmpfs','/var/lib/mysql:rw,size=256m','-e','MARIADB_ALLOW_EMPTY_ROOT_PASSWORD=1','-e','MARIADB_DATABASE=internet_test','mariadb:11.4','--innodb-buffer-pool-size=32M','--innodb-log-file-size=16M',stdout=subprocess.DEVNULL)
 for attempt in range(60):
  p=subprocess.run(D+['exec','mkr-v21-db','mariadb-admin','ping','--silent'],env=ENV,capture_output=True)
  if p.returncode==0:break
  time.sleep(.5)
 else:raise RuntimeError('test database did not start')
 docker('run','-d','--name','mkr-v21-web','--network','mkr-v21-net','-p','4201:8080','-v',str(app.resolve())+':/app:ro','-w','/app',image,php,'-S','0.0.0.0:8080','-t','/app',stdout=subprocess.DEVNULL)
 for attempt in range(30):
  try:
   with urllib.request.urlopen('http://localhost:4201/api/account.php?action=me',timeout=1):break
  except OSError:time.sleep(.1)
 else:raise RuntimeError('test web server did not start')
def stop():
 subprocess.run(D+['rm','-f','mkr-v21-web','mkr-v21-db'],env=ENV,capture_output=True)
 subprocess.run(D+['network','rm','mkr-v21-net'],env=ENV,capture_output=True)
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('action',choices=['start','stop']);p.add_argument('--work',type=Path,default=Path('/tmp/mkr-web-test'));p.add_argument('--image',default='mkr-test-php');p.add_argument('--php',default='php');a=p.parse_args()
 if a.action=='start':start(a.work,a.image,a.php)
 else:stop()

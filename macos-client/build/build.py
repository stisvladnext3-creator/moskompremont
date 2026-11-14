#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,json,os,plistlib,re,shutil,subprocess,tarfile,urllib.request,zipfile,io,sys
from macho import audit
OWN=Path(__file__).resolve().parents[1];REPO=OWN.parent
p=argparse.ArgumentParser();p.add_argument('--work',type=Path,required=True);p.add_argument('--version',required=True);p.add_argument('--flavor',choices=['legacy','modern'],required=True);p.add_argument('--rid',choices=['osx-x64','osx-arm64'],required=True);a=p.parse_args();work=a.work.resolve();work.mkdir(exist_ok=True,parents=True)
assert re.fullmatch(r'0\.2\.\d+',a.version);src=json.loads((OWN/'build/sources.json').read_text());arch=a.rid.removeprefix('osx-');minimum='11.0' if a.flavor=='legacy' else '13.0'
def run(*args,**kw):return subprocess.run(list(map(str,args)),check=True,**kw)
def download(url,path,sha=None):
 path.parent.mkdir(exist_ok=True,parents=True)
 with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'MosKomRemont-build'}),timeout=120) as response,path.open('wb') as out:shutil.copyfileobj(response,out)
 if sha and hashlib.sha256(path.read_bytes()).hexdigest()!=sha:raise ValueError('Checksum mismatch: '+path.name)
 return path
upstream=work/'upstream';run('git','clone','--quiet','--depth','1','--branch',src['upstream']['version'],'https://github.com/2dust/v2rayN.git',upstream)
assert subprocess.check_output(['git','-C',str(upstream),'rev-parse','HEAD'],text=True).strip()==src['upstream']['commit']
run('git','-C',upstream,'submodule','update','--init','--recursive','--depth','1')
run('python3',OWN/'build/prepare.py',upstream,'--version',a.version,'--flavor',a.flavor,'--rid',a.rid)
app=work/'MoskomVPN.app';payload=app/'Contents/MacOS';resources=app/'Contents/Resources';resources.mkdir(parents=True);payload.mkdir(parents=True)
run('dotnet','publish',upstream/'v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj','-c','Release','-r',a.rid,'--self-contained','true','-p:PublishSingleFile=false','-p:DebugType=None','-p:Version='+a.version,'-o',payload)
(payload/'v2rayN').rename(payload/'MoskomVPN')
run('clang','-dynamiclib','-fobjc-arc','-mmacosx-version-min='+minimum,'-arch','x86_64' if arch=='x64' else 'arm64','-framework','Foundation','-framework','Security','-framework','SystemConfiguration','-install_name','@rpath/libMoskomPlatform.dylib',OWN/'native/MoskomPlatform.m','-o',payload/'libMoskomPlatform.dylib')
# The upstream SQLite native package now targets macOS 13.5. Compile its same
# public-domain SQLite engine for the advertised OS rather than editing Mach-O headers.
sqlite=src['sqlite'];archive=download(sqlite['url'],work/'sqlite-source.zip',sqlite['sha256'])
with zipfile.ZipFile(archive) as z:
 name=next(n for n in z.namelist() if n.endswith('/sqlite3.c'));(work/'sqlite3.c').write_bytes(z.read(name))
run('clang','-dynamiclib','-O2','-mmacosx-version-min='+minimum,'-arch','x86_64' if arch=='x64' else 'arm64','-DSQLITE_THREADSAFE=1','-DSQLITE_ENABLE_COLUMN_METADATA','-DSQLITE_ENABLE_FTS3','-DSQLITE_ENABLE_FTS4','-DSQLITE_ENABLE_FTS5','-DSQLITE_ENABLE_RTREE','-DSQLITE_ENABLE_DBSTAT_VTAB','-DSQLITE_ENABLE_MATH_FUNCTIONS','-DSQLITE_ENABLE_SESSION','-DSQLITE_ENABLE_PREUPDATE_HOOK','-DSQLITE_ENABLE_SNAPSHOT','-DSQLITE_ENABLE_UNLOCK_NOTIFY','-install_name','@rpath/libe_sqlite3.dylib',work/'sqlite3.c','-o',payload/'libe_sqlite3.dylib')
audit(payload,arch,tuple(map(int,minimum.split('.')))+(0,))
# Cores are self-contained. Legacy sing-box is built with the last Go branch
# whose genuine Darwin deployment target includes Big Sur; no header patching.
bin=payload/'bin';bin.mkdir(exist_ok=True)
for core,spec in src['cores'][a.flavor].items():
 coreDir=bin/('xray' if core=='xray' else 'sing_box');coreDir.mkdir(exist_ok=True);corePath=coreDir/core
 if 'assets' in spec:
  asset=spec['assets'][arch];archive=download(asset['url'],work/(core+('.zip' if core=='xray' else '.tar.gz')),asset['sha256'])
  if core=='xray':
   with zipfile.ZipFile(archive) as z:data=z.read(next(n for n in z.namelist() if n.split('/')[-1]=='xray'))
  else:
   with tarfile.open(archive) as t:data=t.extractfile(next(n for n in t.getnames() if n.split('/')[-1]=='sing-box')).read()
  corePath.write_bytes(data)
 else:
  tree=work/'sing-box-source';run('git','clone','--quiet','--depth','1','--branch',spec['version'],spec['repository'],tree)
  assert subprocess.check_output(['git','-C',str(tree),'rev-parse','HEAD'],text=True).strip()==spec['commit']
  env=os.environ.copy();env.update({'GOTOOLCHAIN':'local','CGO_ENABLED':'0','GOOS':'darwin','GOARCH':'amd64' if arch=='x64' else 'arm64'})
  run('go','build','-trimpath','-tags',spec['tags'],'-ldflags','-s -w -X github.com/sagernet/sing-box/constant.Version='+spec['version'].lstrip('v'),'-o',corePath,'./cmd/sing-box',cwd=tree,env=env)
 corePath.chmod(0o755)
preset=payload/'preset-russia';preset.mkdir();russia=src['russia'];base=f'https://raw.githubusercontent.com/runetfreedom/russia-v2ray-custom-routing-list/{russia["routingCommit"]}/v2rayN/'
download(base+'template.json',preset/'template.json');template=json.loads((preset/'template.json').read_text());allrules=[]
for i,rule in enumerate(template['routingItems']):
 file=download(base+rule['url'].split('/')[-1],preset/f'route-{i}.json');allrules.append(file.read_text())
for name in ['v2ray.json','sing_box.json','simple_dns.json']:
 file=download(base+name,preset/name);doc=json.loads(file.read_text())
 if name!='simple_dns.json':
  for field in ['normalDNS','tunDNS']:
   if isinstance(doc.get(field),str) and doc[field].startswith(('https://raw.githubusercontent.com/runetfreedom/','https://cdn.jsdelivr.net/gh/runetfreedom/')):
    dns=download(base+doc[field].split('/')[-1],work/('dns-'+doc[field].split('/')[-1]));doc[field]=dns.read_text()
  file.write_text(json.dumps(doc,ensure_ascii=False))
 allrules.append(file.read_text())
for asset in russia['files']:download(asset['url'],bin/asset['name'],asset['sha256'])
for kind,tag in sorted(set(re.findall(r'(geoip|geosite):([A-Za-z0-9_@!.-]+)','\n'.join(allrules)))):
 name=f'{kind}-{tag}.srs';download(f'https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/{russia["rulesCommit"]}/sing-box/rule-set-{kind}/{name}',bin/'srss'/name)
(preset/'manifest.json').write_text(json.dumps({'routingCommit':russia['routingCommit'],'rulesCommit':russia['rulesCommit'],'geoRelease':russia['geoRelease'],'fetchedAt':'2026-11-15T00:00:00Z'}))
# Generate Dock/Finder icon from the same original site logo used on Windows.
icons=work/'Moskom.iconset';icons.mkdir()
for size in [16,32,128,256,512]:
 for scale in [1,2]:
  name=f'icon_{size}x{size}'+('@2x' if scale==2 else '')+'.png';run('sips','-z',size*scale,size*scale,REPO/'desktop-client/branding/app-logo.png','--out',icons/name,stdout=subprocess.DEVNULL)
run('iconutil','-c','icns',icons,'-o',resources/'Moskom.icns')
info={'CFBundleExecutable':'MoskomVPN','CFBundleIdentifier':'ru.moskompremont.vpn'+('.legacy' if a.flavor=='legacy' else ''),'CFBundleName':'МосКомпРемонт VPN','CFBundleDisplayName':'МосКомпРемонт VPN','CFBundleShortVersionString':a.version,'CFBundleVersion':a.version,'CFBundlePackageType':'APPL','CFBundleIconFile':'Moskom.icns','LSMinimumSystemVersion':minimum,'NSHighResolutionCapable':True,'NSLocalNetworkUsageDescription':'Проверка подключения к VPN и локальному прокси МосКомпРемонт.','MoskomBuildFlavor':a.flavor,'MoskomArchitecture':arch}
with (app/'Contents/Info.plist').open('wb') as f:plistlib.dump(info,f)
shutil.copyfile(REPO/'desktop-client/LICENSE',resources/'LICENSE-v2rayN-GPL-3.0.txt');shutil.copyfile(REPO/'desktop-client/THIRD-PARTY-NOTICES.txt',resources/'THIRD-PARTY-NOTICES.txt');shutil.copyfile(OWN/'build/sources.json',resources/'sources.json')
# Do not publish any native library that demands an OS above the advertised target.
report=audit(payload,arch,tuple(map(int,minimum.split('.')))+(0,));(work/'native-audit.json').write_text(json.dumps(report,indent=2))
for item in report:
 file=payload/item['path']
 if file==payload/'MoskomVPN':continue
 run('codesign','--force','--sign','-','--timestamp=none',file,stdout=subprocess.DEVNULL)
run('codesign','--force','--sign','-','--timestamp=none',app,stdout=subprocess.DEVNULL)
run('codesign','--verify','--deep','--strict',app)
(work/'build.json').write_text(json.dumps({'version':a.version,'flavor':a.flavor,'rid':a.rid,'minimumOS':minimum,'upstream':src['upstream']['commit']},indent=2))
print('Built',app,'for macOS',minimum,'and',arch,flush=True)

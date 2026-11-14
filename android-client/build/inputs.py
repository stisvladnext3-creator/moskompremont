from pathlib import Path
import json,sys,urllib.request,hashlib
root=Path(sys.argv[1]);sources=json.loads(Path(__file__).with_name('sources.json').read_text())
def get(url,to,sha=None):
 to.parent.mkdir(parents=True,exist_ok=True)
 with urllib.request.urlopen(url,timeout=90) as r: data=r.read()
 if sha:assert hashlib.sha256(data).hexdigest()==sha,to.name
 to.write_bytes(data)
get(sources['aar']['url'],root/'V2rayNG/app/libs/libv2ray.aar',sources['aar']['sha256'])
assets=root/'V2rayNG/app/src/main/assets';preset=assets/'moskom/russia'
base=f"https://raw.githubusercontent.com/runetfreedom/russia-v2ray-custom-routing-list/{sources['russia']['routingCommit']}/v2rayN/"
get(base+'template.json',preset/'template.json')
template=json.loads((preset/'template.json').read_text())
for i,item in enumerate(template['routingItems']):get(base+item['url'].rsplit('/',1)[-1],preset/f'route-{i}.json')
get(base+'simple_dns.json',preset/'simple_dns.json')
for item in sources['russia']['files']:
 if item['name'] in ('geoip.dat','geosite.dat'):get(item['url'],assets/item['name'],item['sha256'])
(preset/'manifest.json').write_text(json.dumps(sources['russia']))
print('Pinned native AAR and Russia assets downloaded and verified.')

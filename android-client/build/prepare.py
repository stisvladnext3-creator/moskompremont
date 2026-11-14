"""Apply the branded Android overlay to the pinned, unmodified v2rayNG source."""
from pathlib import Path
import sys, shutil, re

root=Path(sys.argv[1]).resolve(); own=Path(__file__).resolve().parents[1]
version=sys.argv[2]; code=int(sys.argv[3])
assert re.fullmatch(r'\d+\.\d+\.\d+',version)
def edit(name,old,new):
 p=root/name;s=p.read_text(encoding='utf-8-sig')
 if s.count(old)!=1:raise SystemExit(f'Upstream anchor mismatch in {name}: {old[:90]}')
 p.write_text(s.replace(old,new),encoding='utf-8')
for f in (own/'overlay').rglob('*'):
 if f.is_file():
  to=root/'V2rayNG/app/src'/f.relative_to(own/'overlay');to.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,to)
gradle='V2rayNG/app/build.gradle.kts'
edit(gradle,'applicationId = "com.v2ray.ang"','applicationId = "ru.moskompremont.vpn"')
edit(gradle,'applicationIdSuffix = ".fdroid"','// Direct APK distribution uses the service application ID.')
edit(gradle,'versionCode = 755',f'versionCode = {code}')
edit(gradle,'versionName = "2.3.15"',f'versionName = "{version}"')
# The service UI follows the Russian Windows client; retain English fallback resources.
g=root/gradle;s=g.read_text();s=re.sub(r'localeFilters \+= listOf\(.*?\n        \)', 'localeFilters += listOf("en", "ru")',s,flags=re.S);g.write_text(s)
res=root/'V2rayNG/app/src/main/res';(res/'drawable').mkdir(exist_ok=True)
shutil.copyfile(own.parent/'desktop-client/branding/app-logo.png',res/'drawable/moskom_logo.png')
manifest='V2rayNG/app/src/main/AndroidManifest.xml'
edit(manifest,'android:icon="@mipmap/ic_launcher"','android:icon="@drawable/moskom_logo"')
# Do not export account/session data through Android backup.
edit(manifest,'android:allowBackup="true"','android:allowBackup="false"')
for p in res.glob('values*/strings.xml'):
 s=p.read_text();s=re.sub(r'(<string name="app_name"[^>]*>).*?(</string>)',r'\1МосКомпРемонт VPN\2',s);p.write_text(s)
print(f'Prepared MosKomRemont Android {version}')

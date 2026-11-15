"""Install code-free package fixtures only on the disposable CI emulator.

No third-party APK, artwork, account or bank connection is used. The real
VpnService UID exclusions are checked with these installed package identities.
"""
from pathlib import Path
import os, subprocess, tempfile
sdk=Path(os.environ['ANDROID_HOME']);tools=sdk/'build-tools/37.0.0';android=sdk/'platforms/android-37.0/android.jar'
keystore=Path.home()/'.android/debug.keystore';assert keystore.is_file() and android.is_file()
with tempfile.TemporaryDirectory(prefix='moskom-domestic-fixtures-') as tmp:
 root=Path(tmp)
 for index,package in enumerate(['com.punicapp.whoosh','ru.yandex.yandexmaps','com.sberbankmobile']):
  manifest=root/f'manifest-{index}.xml';apk=root/f'fixture-{index}.apk'
  manifest.write_text(f'<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="{package}" android:versionCode="1" android:versionName="1"><uses-sdk android:minSdkVersion="24" android:targetSdkVersion="35"/><application android:hasCode="false" android:label="Routing fixture"/></manifest>')
  subprocess.run([str(tools/'aapt2'),'link','-I',str(android),'--manifest',str(manifest),'-o',str(apk)],check=True)
  subprocess.run([str(tools/'apksigner'),'sign','--ks',str(keystore),'--ks-pass','pass:android','--key-pass','pass:android',str(apk)],check=True)
  subprocess.run(['adb','install','-r',str(apk)],check=True)
print('Installed three code-free domestic package fixtures on the CI emulator.')

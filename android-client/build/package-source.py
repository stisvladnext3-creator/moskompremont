"""Create a rebuildable GPL source bundle without generated files or signing material."""
from pathlib import Path
import json,sys,zipfile,subprocess

root=Path(sys.argv[1]).resolve();repo=Path(__file__).resolve().parents[2];out=Path(sys.argv[2]);version=sys.argv[3]
excluded={'.git','.gradle','.idea','.cxx','build','node_modules','__pycache__'}
prefix=f'MoskomVPN-Android-{version}-source'
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
 for source in root.rglob('*'):
  relative=source.relative_to(root)
  if source.is_file() and not any(part in excluded for part in relative.parts) and source.suffix.lower() not in {'.aar','.so','.apk','.jks','.p12'} and source.name!='local.properties':
   archive.write(source,f'{prefix}/v2rayNG/{relative}')
 for folder in ('android-client','desktop-client/branding'):
  for source in (repo/folder).rglob('*'):
   relative=source.relative_to(repo)
   if source.is_file() and '__pycache__' not in relative.parts:archive.write(source,f'{prefix}/moskom/{relative}')
 help_source=repo/'desktop-client/overlay/v2rayN.Desktop/Views/MoskomHelpView.cs'
 archive.write(help_source,f'{prefix}/moskom/{help_source.relative_to(repo)}')
 archive.write(repo/'.github/workflows/android-vpn.yml',f'{prefix}/moskom/.github/workflows/android-vpn.yml')
 instructions='''МосКомпРемонт VPN для Android — исходный код\n\nБаза: v2rayNG 2.3.15; точные ревизии, лицензии и входные файлы указаны в moskom/android-client/build/sources.json и v2rayNG/.\n\nИсходники в папке v2rayNG уже содержат изменения оболочки. Для сборки установите Java 21, Android SDK 37 и NDK из workflow. Восстановите сгенерированные HEV библиотеки командой compile-hevtun.sh, скопируйте libs в V2rayNG/app, затем выполните inputs.py (загрузит закреплённую AAR и проверенные геоданные). Не запускайте prepare.py повторно на изменённых исходниках. Из v2rayNG/V2rayNG выполните ./gradlew :app:assembleFdroidRelease. Для применения overlay к чистой закреплённой базе используйте prepare.py. Подробный процесс указан в приложенном workflow.\n\nКлюч подписи релиза в этот архив не входит. Для установки обновлений поверх опубликованного APK нужен исходный ключ владельца.\n'''
 archive.writestr(f'{prefix}/BUILD-RU.txt',instructions)
print(f'Created source bundle: {out.name} ({out.stat().st_size} bytes)')

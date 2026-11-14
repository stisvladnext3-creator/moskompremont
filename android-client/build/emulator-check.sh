#!/usr/bin/env bash
set -eu
mkdir -p "$RUNNER_TEMP/moskom-result"
collect_review() {
  adb pull /sdcard/Android/data/ru.moskompremont.vpn/files/review "$RUNNER_TEMP/moskom-result/" || true
  adb logcat -d > "$RUNNER_TEMP/moskom-result/emulator-logcat.txt" || true
}
trap collect_review EXIT
# Android 15 Google APIs' Pixel Launcher can ANR during headless boot and cover the VPN consent.
# Only the CI launcher is disabled. Remove after an emulator image passes cold boot without this ANR.
adb shell pm disable-user --user 0 com.google.android.apps.nexuslauncher || true
cd "$RUNNER_TEMP/moskom-android/V2rayNG"
if ! ./gradlew :app:connectedFdroidDebugAndroidTest --stacktrace; then
  python - <<'PY'
from pathlib import Path
import xml.etree.ElementTree as ET
for file in Path('app/build/outputs/androidTest-results').rglob('*.xml'):
    for failure in ET.parse(file).getroot().iter('failure'):
        print((failure.text or failure.get('message',''))[:4500])
PY
  exit 1
fi

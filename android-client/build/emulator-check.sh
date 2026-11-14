#!/usr/bin/env bash
set -eu
mkdir -p "$RUNNER_TEMP/moskom-result"
collect_review() {
  adb pull /sdcard/Android/data/ru.moskompremont.vpn/files/review "$RUNNER_TEMP/moskom-result/" || true
  adb logcat -d > "$RUNNER_TEMP/moskom-result/emulator-logcat.txt" || true
}
trap collect_review EXIT
cd "$RUNNER_TEMP/moskom-android/V2rayNG"
./gradlew :app:connectedFdroidDebugAndroidTest --stacktrace

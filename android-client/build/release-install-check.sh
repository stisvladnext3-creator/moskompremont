#!/usr/bin/env bash
# Exercise the actual publisher-signed APK, not the debug APK used by UI tests.
set -euo pipefail
trap 'echo "Release installation check failed at line $LINENO" >&2' ERR

package=ru.moskompremont.vpn
activity=com.v2ray.ang.ui.main.MainActivity
apk="$GITHUB_WORKSPACE/release-payload/android-$MOSKOM_RELEASE_VERSION/MoskomVPN-Android-$MOSKOM_RELEASE_VERSION.apk"
previous="$RUNNER_TEMP/previous-android/MoskomVPN-Android-0.1.29.apk"
api=$(adb shell getprop ro.build.version.sdk | tr -d '\r')
reports="$RUNNER_TEMP/signed-android-installation/api-$api"
mkdir -p "$reports"
exec > >(tee "$reports/install.txt") 2>&1

assert_version() {
    adb shell dumpsys package "$package" | tr -d '\r' > "$reports/package.txt"
    grep -Fx "    versionName=$MOSKOM_RELEASE_VERSION" "$reports/package.txt"
}

assert_launch() {
    adb logcat -c
    adb shell am start -W -n "$package/$activity"
    sleep 3
    adb shell pidof "$package"
    adb logcat -d -b crash > "$reports/crash.txt"
    if grep -F "Process: $package," "$reports/crash.txt"; then
        echo 'The release application crashed during startup' >&2
        exit 1
    fi
}

package_id() {
    # Android 15 calls this appId; Android 7 calls it userId.
    # These checks use the primary emulator user (user 0), so they are the UID.
    sed -nE 's/^ *(userId|appId)=([0-9]+)$/\2/p' "$1" | head -n 1
}

allow_notifications() {
    if (( api >= 33 )); then
        # Keep the Android 13+ notification prompt from covering the test UI.
        adb shell pm grant "$package" android.permission.POST_NOTIFICATIONS
    fi
}

# The Google APIs emulator is rootable; no rooted customer device is required.
adb root
adb wait-for-device
adb uninstall "$package" >/dev/null 2>&1 || true
adb install "$apk"
allow_notifications
assert_version
assert_launch
adb shell am force-stop "$package"
adb uninstall "$package"

adb install "$previous"
allow_notifications
adb shell am start -W -n "$package/$activity"
adb shell am force-stop "$package"
adb shell dumpsys package "$package" | tr -d '\r' > "$reports/previous-package.txt"
old_uid=$(package_id "$reports/previous-package.txt")
old_install=$(sed -n 's/^ *firstInstallTime=//p' "$reports/previous-package.txt" | head -n 1)
test -n "$old_uid"
test -n "$old_install"
marker="/data/user/0/$package/files/moskom-release-upgrade-test"
adb shell "mkdir -p /data/user/0/$package/files && echo saved-before-upgrade > $marker"

adb install -r "$apk"
assert_version
test "$(package_id "$reports/package.txt")" = "$old_uid"
test "$(sed -n 's/^ *firstInstallTime=//p' "$reports/package.txt" | head -n 1)" = "$old_install"
test "$(adb shell cat "$marker" | tr -d '\r')" = saved-before-upgrade
assert_launch
adb exec-out screencap -p > "$reports/updated-app.png"
echo "PASS: production APK clean install, 0.1.29 upgrade preserving UID and private data, startup on API $api"

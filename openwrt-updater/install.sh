#!/bin/sh
# Install the MosKomRemont OpenWrt updater from a GitHub release.
# Usage: wget -qO- https://raw.githubusercontent.com/stisvladnext3-creator/moskompremont/main/openwrt-updater/install.sh | sh
set -eu

RELEASE_VERSION="${MKR_RELEASE_VERSION:-0.1.5}"
case "$RELEASE_VERSION" in
  *[!0-9.]*|'') echo "Некорректная версия релиза." >&2; exit 1 ;;
esac
RELEASE_TAG="${MKR_RELEASE_TAG:-moskom-openwrt-v$RELEASE_VERSION}"
BASE_URL="${MKR_RELEASE_URL:-https://github.com/stisvladnext3-creator/moskompremont/releases/download/$RELEASE_TAG}"
TMP_DIR="$(mktemp -d /tmp/moskom-openwrt-install.XXXXXX)"

cleanup() { rm -rf "$TMP_DIR"; }
trap cleanup EXIT INT TERM
umask 077

download() {
  url="$1"; destination="$2"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL --retry 2 "$url" -o "$destination"
  elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$destination" "$url"
  else
    echo "Нужен curl или wget для загрузки пакетов." >&2
    exit 1
  fi
}

verify_and_download() {
  file="$1"
  download "$BASE_URL/$file" "$TMP_DIR/$file"
  expected="$(awk -v wanted="$file" '$2 == wanted { print $1 }' "$TMP_DIR/SHA256SUMS.txt")"
  [ -n "$expected" ] || { echo "В SHA256SUMS.txt нет записи для $file." >&2; exit 1; }
  actual="$(sha256sum "$TMP_DIR/$file" | awk '{print $1}')"
  [ "$actual" = "$expected" ] || { echo "Несовпадение SHA-256 для $file." >&2; exit 1; }
}

download "$BASE_URL/SHA256SUMS.txt" "$TMP_DIR/SHA256SUMS.txt"

if command -v apk >/dev/null 2>&1; then
  verify_and_download "mkr-router-updater-$RELEASE_VERSION-r1.apk"
  verify_and_download "luci-app-mkr-router-$RELEASE_VERSION-r1.apk"
  apk update
  # The release is distributed with a checksum and is intentionally not signed
  # by the OpenWrt image key. This is scoped to these two verified files only.
  apk add --allow-untrusted \
    "$TMP_DIR/mkr-router-updater-$RELEASE_VERSION-r1.apk" \
    "$TMP_DIR/luci-app-mkr-router-$RELEASE_VERSION-r1.apk"
elif command -v opkg >/dev/null 2>&1; then
  verify_and_download "mkr-router-updater_${RELEASE_VERSION}-1_all.ipk"
  verify_and_download "luci-app-mkr-router_${RELEASE_VERSION}-1_all.ipk"
  opkg update
  opkg install "$TMP_DIR/mkr-router-updater_${RELEASE_VERSION}-1_all.ipk"
  opkg install "$TMP_DIR/luci-app-mkr-router_${RELEASE_VERSION}-1_all.ipk"
else
  echo "Не найден apk или opkg. Скрипт предназначен для OpenWrt 23.05+ и 25.12+." >&2
  exit 1
fi

echo "МосКомпРемонт установлен. Откройте в LuCI: Сервисы → МосКомпРемонт."

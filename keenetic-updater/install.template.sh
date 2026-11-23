#!/bin/sh
# MosKomRemont XKeen updater. HTTPS delivery + a pinned package digest.
set +x
set -eu
umask 077
VERSION='@VERSION@'
PACKAGE='mkr-xkeen-updater_@VERSION@-1_all.ipk'
SHA256='@SHA256@'
endpoint=${1:-https://moskompremont.ru/api/router-agent.php}
[ "$(id -u)" = 0 ] || { echo 'Подключитесь к Entware по SSH как root.' >&2; exit 1; }
[ -x /opt/bin/opkg ] || { echo 'Сначала установите Entware и работающий XKeen с Xray.' >&2; exit 1; }
PATH=/opt/bin:/opt/sbin:/usr/bin:/bin:/usr/sbin:/sbin
export PATH
command -v xkeen >/dev/null 2>&1 && command -v xray >/dev/null 2>&1 || { echo 'Нужен уже настроенный XKeen с Xray.' >&2; exit 1; }
case "$endpoint" in https://*/api/router-agent.php) :;; *) echo 'Нужен HTTPS-адрес /api/router-agent.php вашего сайта.' >&2; exit 1;; esac
case "$endpoint" in *[!A-Za-z0-9_./:\[\]~-]*|*..*) echo 'Некорректный адрес API.' >&2; exit 1;; esac
base=${endpoint%/api/router-agent.php}/downloads/keenetic/$VERSION
if ! command -v sha256sum >/dev/null 2>&1; then
 opkg update
 opkg install coreutils-sha256sum
fi
temp=$(mktemp -d /tmp/mkr-xkeen-install.XXXXXX)
trap 'rm -rf "$temp"' 0
trap 'exit 130' HUP INT TERM
echo 'Загружаем пакет XKeen с сайта сервиса…'
curl --proto '=https' --proto-redir '=https' -fL --connect-timeout 15 --max-time 90 --max-filesize 524288 "$base/$PACKAGE" -o "$temp/$PACKAGE"
printf '%s  %s\n' "$SHA256" "$temp/$PACKAGE" | sha256sum -c - || { echo 'Проверка пакета не пройдена. Установка отменена.' >&2; exit 1; }
echo 'Устанавливаем пакет и необходимые инструменты Entware…'
opkg update
opkg install "$temp/$PACKAGE"
# Entware's cron owns a user crontab, without the extra "root" column.
if ! pidof crond >/dev/null 2>&1; then
 for init in /opt/etc/init.d/S*cron; do [ ! -x "$init" ] || "$init" start; done
fi
echo 'Осталось вставить код устройства из кабинета.'
/opt/bin/mkr-xkeen setup "$endpoint"
echo 'Готово. Проверить состояние: mkr-xkeen status'

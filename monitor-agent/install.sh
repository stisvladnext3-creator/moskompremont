#!/usr/bin/env bash
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo 'Запустите: sudo bash install.sh' >&2; exit 1; fi
monitor_source_dir="$(cd -- "$(dirname -- "$0")" && pwd)"
for monitor_dependency in python3 curl; do command -v "$monitor_dependency" >/dev/null || { echo 'Установите python3 curl ca-certificates.' >&2; exit 1; }; done
monitor_xray_path="${XRAY_BIN:-}"
if [ -z "$monitor_xray_path" ]; then
 for monitor_candidate in /usr/local/x-ui/bin/xray-linux-amd64 /usr/local/x-ui/bin/xray-linux-arm64 /usr/local/bin/xray /usr/bin/xray; do
  if [ -x "$monitor_candidate" ]; then monitor_xray_path="$monitor_candidate"; break; fi
 done
fi
if [ ! -x "$monitor_xray_path" ]; then echo 'Xray не найден. Укажите путь: sudo XRAY_BIN=/путь/к/xray bash install.sh' >&2; exit 1; fi
"$monitor_xray_path" version >/dev/null
if ! id mkr-monitor >/dev/null 2>&1; then useradd --system --home /nonexistent --shell /usr/sbin/nologin mkr-monitor; fi
install -d -m 0755 /opt/moskom-monitor /opt/moskom-monitor/bin
install -m 0755 "$monitor_source_dir/monitor.py" /opt/moskom-monitor/monitor.py
install -m 0755 "$monitor_xray_path" /opt/moskom-monitor/bin/xray
install -d -o root -g mkr-monitor -m 0750 /etc/moskom-monitor
python3 "$monitor_source_dir/setup.py"
install -m 0644 "$monitor_source_dir/moskom-monitor.service" /etc/systemd/system/moskom-monitor.service
systemctl daemon-reload
systemctl enable moskom-monitor.service
systemctl restart moskom-monitor.service
echo 'Служба установлена. Проверка: sudo systemctl status moskom-monitor --no-pager'
echo 'Журнал: sudo journalctl -u moskom-monitor -n 30 --no-pager'

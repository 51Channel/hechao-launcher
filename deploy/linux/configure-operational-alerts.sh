#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-operational-alerts.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 3 ]]; then
  echo "usage: configure-operational-alerts.sh <monitor-python> <service-file> <timer-file>" >&2
  exit 1
fi

monitor_python="$1"
service_file="$2"
timer_file="$3"
api_environment="/etc/hechao-launcher-api/environment"
monitor_root="/opt/hechao-platform-monitor"
monitor_state_root="/var/lib/hechao-platform-monitor"
monitor_environment_root="/etc/hechao-platform-monitor"
monitor_environment="${monitor_environment_root}/environment"

test -f "$monitor_python"
test -f "$service_file"
test -f "$timer_file"
test -f "$api_environment"

install -d -o root -g root -m 0755 "$monitor_root"
install -d -o root -g root -m 0700 "$monitor_state_root"
install -d -o root -g root -m 0700 "$monitor_environment_root"
install -o root -g root -m 0555 \
  "$monitor_python" "${monitor_root}/hechao-platform-monitor.py"
install -o root -g root -m 0644 \
  "$service_file" /etc/systemd/system/hechao-platform-monitor.service
install -o root -g root -m 0644 \
  "$timer_file" /etc/systemd/system/hechao-platform-monitor.timer

token=""
if [[ -f "$monitor_environment" ]]; then
  token="$(
    sed -n 's/^HECHAO_MONITOR_TOKEN=//p' "$monitor_environment" |
      head -n 1
  )"
fi
if [[ "${#token}" -lt 32 ]]; then
  token="$(openssl rand -base64 48 | tr -d '\n')"
fi
token_sha256="$(printf '%s' "$token" | sha256sum | awk '{print $1}')"

api_environment_next="$(mktemp "${api_environment}.next.XXXXXX")"
awk '
  !/^OperationalAlerts__Enabled=/ &&
  !/^OperationalAlerts__InternalTokenSha256=/ &&
  !/^OperationalAlerts__EvaluationSeconds=/ &&
  !/^OperationalAlerts__EvaluationWindowMinutes=/ &&
  !/^OperationalAlerts__RequestMetricsRetentionDays=/
' "$api_environment" > "$api_environment_next"
cat >> "$api_environment_next" <<EOF
OperationalAlerts__Enabled=true
OperationalAlerts__InternalTokenSha256=${token_sha256}
OperationalAlerts__EvaluationSeconds=60
OperationalAlerts__EvaluationWindowMinutes=15
OperationalAlerts__RequestMetricsRetentionDays=30
EOF
install -o root -g root -m 0600 "$api_environment_next" "$api_environment"
rm -f -- "$api_environment_next"

monitor_environment_next="$(
  mktemp "${monitor_environment_root}/environment.next.XXXXXX"
)"
cat > "$monitor_environment_next" <<EOF
HECHAO_MONITOR_API_BASE_URL=http://127.0.0.1:8090
HECHAO_MONITOR_TOKEN=${token}
HECHAO_MONITOR_STATE_PATH=/var/lib/hechao-platform-monitor/state.json
HECHAO_MONITOR_SMTP_ENV_FILE=/home/ecs-user/hechao/.env
HECHAO_MONITOR_HTTP_TIMEOUT_SECONDS=10
HECHAO_MONITOR_BACKUP_RECEIPT_PATH=/var/lib/hechao-offsite-backup/latest.json
HECHAO_MONITOR_BACKUP_FAILURE_PATH=/var/lib/hechao-offsite-backup/failure.json
EOF
install -o root -g root -m 0600 \
  "$monitor_environment_next" "$monitor_environment"
rm -f -- "$monitor_environment_next"

python3 "${monitor_root}/hechao-platform-monitor.py" --self-test >/dev/null
systemd-analyze verify \
  /etc/systemd/system/hechao-platform-monitor.service \
  /etc/systemd/system/hechao-platform-monitor.timer
systemctl daemon-reload

echo "Operational alerting is configured; the monitor timer remains inactive."

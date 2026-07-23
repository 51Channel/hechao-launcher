#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-admin-web.sh must run as root" >&2
  exit 1
fi

enabled="${1:-false}"
if [[ "$enabled" != "true" && "$enabled" != "false" ]]; then
  echo "usage: configure-admin-web.sh [true|false]" >&2
  exit 1
fi

if ! id hechao-api >/dev/null 2>&1; then
  echo "service account hechao-api does not exist" >&2
  exit 1
fi

environment_file="/etc/hechao-launcher-api/environment"
key_path="/var/lib/hechao-launcher-api/data-protection"
backup_root="/var/backups/hechao-launcher/api-configuration"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
temporary_file="$(mktemp)"
trap 'rm -f "$temporary_file"' EXIT

install -d -o root -g root -m 0700 "$backup_root"
if [[ -f "$environment_file" ]]; then
  install -o root -g root -m 0600 \
    "$environment_file" \
    "${backup_root}/environment-before-admin-web-${timestamp}"
  grep -v '^AdminWeb__' "$environment_file" > "$temporary_file" || true
fi

cat >> "$temporary_file" <<EOF
AdminWeb__Enabled=$enabled
AdminWeb__PublicBaseUrl=https://admin.hechao.world
AdminWeb__DataProtectionKeyPath=$key_path
AdminWeb__TicketSeconds=90
AdminWeb__SessionMinutes=30
AdminWeb__EnrollmentMinutes=10
AdminWeb__TotpIssuer=Hechao
EOF

install -d -o root -g root -m 0750 /etc/hechao-launcher-api
install -d -o hechao-api -g hechao-api -m 0700 "$key_path"
install -o root -g root -m 0600 "$temporary_file" "$environment_file"

echo "admin_web_enabled=$enabled"
echo "data_protection_key_path=$key_path"
echo "configuration_backup=${backup_root}/environment-before-admin-web-${timestamp}"
echo "api_restart=not_performed"

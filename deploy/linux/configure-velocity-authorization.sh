#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-velocity-authorization.sh must run as root" >&2
  exit 1
fi

IFS= read -r internal_token_sha256
if [[ ! "$internal_token_sha256" =~ ^[0-9a-f]{64}$ ]]; then
  echo "invalid Velocity authorization token digest" >&2
  exit 1
fi

environment_file="/etc/hechao-launcher-api/environment"
backup_root="/var/backups/hechao-launcher/api-configuration"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
temporary_file="$(mktemp)"
trap 'rm -f "$temporary_file"' EXIT

install -d -o root -g root -m 0700 "$backup_root"
if [[ -f "$environment_file" ]]; then
  install -o root -g root -m 0600 \
    "$environment_file" \
    "${backup_root}/environment-before-velocity-${timestamp}"
  grep -v '^VelocityAuthorization__' "$environment_file" > "$temporary_file" || true
fi

cat >> "$temporary_file" <<EOF
VelocityAuthorization__InternalTokenSha256=$internal_token_sha256
VelocityAuthorization__LaunchGrantMinutes=10
VelocityAuthorization__MaximumLuckPermsAgeMinutes=20
VelocityAuthorization__RequireGrantIpMatch=false
EOF

install -d -o root -g root -m 0750 /etc/hechao-launcher-api
install -o root -g root -m 0600 "$temporary_file" "$environment_file"
echo "velocity_authorization_configuration=ready"
echo "configuration_backup=${backup_root}/environment-before-velocity-${timestamp}"
echo "api_restart=not_performed"

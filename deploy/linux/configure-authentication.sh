#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-authentication.sh must run as root" >&2
  exit 1
fi

IFS= read -r internal_sync_token_sha256
if [[ ! "$internal_sync_token_sha256" =~ ^[0-9a-f]{64}$ ]]; then
  echo "invalid internal sync token digest" >&2
  exit 1
fi

environment_file="/etc/hechao-launcher-api/environment"
temporary_file="$(mktemp)"
trap 'rm -f "$temporary_file"' EXIT

if [[ -f "$environment_file" ]]; then
  grep -v '^Authentication__' "$environment_file" > "$temporary_file" || true
fi

cat >> "$temporary_file" <<EOF
Authentication__EnforceCatalogAuthentication=false
Authentication__AccessTokenMinutes=15
Authentication__RefreshTokenDays=30
Authentication__InternalSyncTokenSha256=$internal_sync_token_sha256
EOF

install -d -o root -g root -m 0750 /etc/hechao-launcher-api
install -o root -g root -m 0600 "$temporary_file" "$environment_file"
echo 'authentication_configuration=ready'

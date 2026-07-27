#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-offsite-database-backup-credentials.sh must run as root" >&2
  exit 1
fi

IFS= read -r oss_access_key_id
IFS= read -r oss_access_key_secret
oss_access_key_id="${oss_access_key_id#$'\xEF\xBB\xBF'}"
oss_access_key_id="${oss_access_key_id%$'\r'}"
oss_access_key_secret="${oss_access_key_secret%$'\r'}"
if [[ ! "$oss_access_key_id" =~ ^[A-Za-z0-9]+$ ]] ||
   [[ ! "$oss_access_key_secret" =~ ^[A-Za-z0-9]+$ ]] ||
   [[ ${#oss_access_key_id} -lt 8 ]] ||
   [[ ${#oss_access_key_id} -gt 128 ]] ||
   [[ ${#oss_access_key_secret} -lt 16 ]] ||
   [[ ${#oss_access_key_secret} -gt 128 ]]; then
  echo "invalid OSS credentials" >&2
  exit 1
fi

configuration_directory="/etc/hechao-offsite-backup"
environment_file="${configuration_directory}/environment"
temporary_file="$(mktemp)"
trap 'rm -f "$temporary_file"' EXIT
umask 077

cat > "$temporary_file" <<EOF
OSS_ACCESS_KEY_ID=$oss_access_key_id
OSS_ACCESS_KEY_SECRET=$oss_access_key_secret
EOF

install -d -o root -g root -m 0700 "$configuration_directory"
install -o root -g root -m 0600 "$temporary_file" "$environment_file"

echo "offsite_backup_credentials=ready"
echo "service_restart=not_performed"

#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-distribution.sh must run as root" >&2
  exit 1
fi

IFS= read -r oss_access_key_id
IFS= read -r oss_access_key_secret
if [[ ! "$oss_access_key_id" =~ ^[A-Za-z0-9]+$ ]] ||
   [[ ! "$oss_access_key_secret" =~ ^[A-Za-z0-9]+$ ]] ||
   [[ ${#oss_access_key_id} -lt 8 ]] ||
   [[ ${#oss_access_key_secret} -lt 16 ]] ||
   [[ ${#oss_access_key_secret} -gt 128 ]]; then
  echo "invalid OSS credentials" >&2
  exit 1
fi

environment_file="/etc/hechao-launcher-api/environment"
manifest_directory="/var/lib/hechao-launcher-api/manifests"
temporary_file="$(mktemp)"
trap 'rm -f "$temporary_file"' EXIT

if [[ -f "$environment_file" ]]; then
  grep -v -E '^(Distribution__|OSS_ACCESS_KEY_ID=|OSS_ACCESS_KEY_SECRET=|OSS_SESSION_TOKEN=)' \
    "$environment_file" > "$temporary_file" || true
fi

cat >> "$temporary_file" <<EOF
Distribution__ManifestDirectory=$manifest_directory
Distribution__MaximumManifestBytes=8388608
Distribution__OssRegion=cn-shanghai
Distribution__OssBucket=hechaoworld
Distribution__OssEndpoint=https://download.hechao.world
Distribution__OssObjectPrefix=objects
Distribution__PresignedUrlSeconds=300
OSS_ACCESS_KEY_ID=$oss_access_key_id
OSS_ACCESS_KEY_SECRET=$oss_access_key_secret
EOF

install -d -o root -g root -m 0750 /etc/hechao-launcher-api
install -d -o root -g hechao-api -m 0750 "$manifest_directory"
install -o root -g root -m 0600 "$temporary_file" "$environment_file"
echo 'distribution_configuration=ready'
echo 'api_restart=not_performed'

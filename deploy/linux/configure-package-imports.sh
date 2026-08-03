#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-package-imports.sh must run as root" >&2
  exit 1
fi

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "usage: $0 <environment-file> <true|false> [publisher-token-sha256]" >&2
  exit 64
fi

environment_file="$1"
enabled="${2,,}"
publisher_token_sha256="${3:-}"
storage_root="/var/lib/hechao-launcher-api/package-imports"
backup_root="/var/backups/hechao-launcher/api-configuration"

if [[ "$enabled" != "true" && "$enabled" != "false" ]]; then
  echo "package import enabled flag must be true or false" >&2
  exit 65
fi
if [[ "$enabled" == "true" &&
      ! "$publisher_token_sha256" =~ ^[0-9a-fA-F]{64}$ ]]; then
  echo "enabled package imports require one publisher token SHA-256" >&2
  exit 65
fi
if [[ ! -f "$environment_file" ]]; then
  echo "environment file does not exist: $environment_file" >&2
  exit 66
fi
if ! id hechao-api >/dev/null 2>&1; then
  echo "service account hechao-api does not exist" >&2
  exit 1
fi

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
temporary_file="$(mktemp "${environment_file}.tmp.XXXXXX")"
trap 'rm -f -- "$temporary_file"' EXIT

install -d -o root -g root -m 0700 "$backup_root"
install -o root -g root -m 0600 \
  "$environment_file" \
  "${backup_root}/environment-before-package-imports-${timestamp}"
grep -v '^PackageImports__' "$environment_file" > "$temporary_file" || true

if [[ "$enabled" == "true" ]]; then
  cat >> "$temporary_file" <<EOF
PackageImports__Enabled=true
PackageImports__StorageRoot=$storage_root
PackageImports__MaximumUploadBytes=4294967296
PackageImports__UploadChunkBytes=8388608
PackageImports__MaximumEntries=50000
PackageImports__MaximumExpandedBytes=21474836480
PackageImports__MaximumEntryBytes=4294967296
PackageImports__MaximumCompressionRatio=250
PackageImports__RetentionDays=14
PackageImports__PublisherLeaseMinutes=30
PackageImports__PublisherAgentFreshnessSeconds=30
PackageImports__PublisherTokenSha256=${publisher_token_sha256,,}
EOF
  install -d -o hechao-api -g hechao-api -m 0700 "$storage_root"
else
  echo 'PackageImports__Enabled=false' >> "$temporary_file"
fi

chown --reference="$environment_file" "$temporary_file"
chmod --reference="$environment_file" "$temporary_file"
mv -f -- "$temporary_file" "$environment_file"
trap - EXIT

echo "configuration_backup=${backup_root}/environment-before-package-imports-${timestamp}"
echo "environment=$environment_file"
echo "enabled=$enabled"
echo "storage_root=$storage_root"
echo "api_restart=not_performed"

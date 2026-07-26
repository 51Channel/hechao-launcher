#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-diagnostic-uploads.sh must run as root" >&2
  exit 1
fi

if ! id hechao-api >/dev/null 2>&1; then
  echo "service account hechao-api does not exist" >&2
  exit 1
fi

environment_file="/etc/hechao-launcher-api/environment"
storage_root="/var/lib/hechao-launcher-api/diagnostics"
backup_root="/var/backups/hechao-launcher/api-configuration"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
temporary_file="$(mktemp)"
trap 'rm -f "$temporary_file"' EXIT

install -d -o root -g root -m 0700 "$backup_root"
if [[ -f "$environment_file" ]]; then
  install -o root -g root -m 0600 \
    "$environment_file" \
    "${backup_root}/environment-before-diagnostic-uploads-${timestamp}"
  grep -v '^DiagnosticUploads__' "$environment_file" > "$temporary_file" || true
fi

cat >> "$temporary_file" <<EOF
DiagnosticUploads__StorageRoot=$storage_root
DiagnosticUploads__UploadTokenMinutes=10
DiagnosticUploads__RetentionDays=14
DiagnosticUploads__MaximumBytes=8388608
DiagnosticUploads__MaximumUploadsPerDay=5
DiagnosticUploads__MaximumBytesPerDay=41943040
DiagnosticUploads__MaximumActiveUploads=10
DiagnosticUploads__CleanupMinutes=60
EOF

install -d -o root -g root -m 0750 /etc/hechao-launcher-api
install -d -o hechao-api -g hechao-api -m 0700 "$storage_root"
install -o root -g root -m 0600 "$temporary_file" "$environment_file"

echo "diagnostic_storage_root=$storage_root"
echo "retention_days=14"
echo "maximum_upload_bytes=8388608"
echo "configuration_backup=${backup_root}/environment-before-diagnostic-uploads-${timestamp}"
echo "api_restart=not_performed"

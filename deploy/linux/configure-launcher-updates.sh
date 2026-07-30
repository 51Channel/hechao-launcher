#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 6 || $# -gt 7 ]]; then
  echo "usage: $0 <environment-file> <version> <minimum-version> <bytes> <sha256> <published-at> [release-notes]" >&2
  exit 64
fi

environment_file="$1"
version="$2"
minimum_version="$3"
installer_bytes="$4"
installer_sha256="$5"
published_at="$6"
release_notes="${7:-}"

version_pattern='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
sha_pattern='^[0-9a-fA-F]{64}$'
if [[ ! "$version" =~ $version_pattern ||
      ! "$minimum_version" =~ $version_pattern ||
      ! "$installer_sha256" =~ $sha_pattern ||
      ! "$installer_bytes" =~ ^[0-9]+$ ||
      "$installer_bytes" -lt 1048576 ||
      "$installer_bytes" -gt 536870912 ]]; then
  echo "launcher update metadata is invalid" >&2
  exit 65
fi

if [[ "$release_notes" == *$'\n'* ||
      "$release_notes" == *$'\r'* ||
      ${#release_notes} -gt 2000 ]]; then
  echo "release notes must be a single line no longer than 2000 characters" >&2
  exit 65
fi

if [[ ! -f "$environment_file" ]]; then
  echo "environment file does not exist: $environment_file" >&2
  exit 66
fi

backup="${environment_file}.launcher-updates.$(date -u +%Y%m%dT%H%M%SZ).bak"
cp --preserve=mode,ownership,timestamps -- "$environment_file" "$backup"
temporary="$(mktemp "${environment_file}.tmp.XXXXXX")"
trap 'rm -f -- "$temporary"' EXIT

grep -v -E '^LauncherUpdates__' "$environment_file" > "$temporary"
escaped_release_notes="${release_notes//\\/\\\\}"
escaped_release_notes="${escaped_release_notes//\"/\\\"}"
cat >> "$temporary" <<EOF
LauncherUpdates__Enabled=true
LauncherUpdates__LatestVersion=$version
LauncherUpdates__MinimumSupportedVersion=$minimum_version
LauncherUpdates__InstallerBytes=$installer_bytes
LauncherUpdates__InstallerSha256=${installer_sha256,,}
LauncherUpdates__PublishedAt=$published_at
LauncherUpdates__ReleaseNotes="$escaped_release_notes"
EOF

chown --reference="$environment_file" "$temporary"
chmod --reference="$environment_file" "$temporary"
mv -f -- "$temporary" "$environment_file"
trap - EXIT

echo "backup=$backup"
echo "environment=$environment_file"
echo "version=$version"

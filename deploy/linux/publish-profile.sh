#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "publish-profile.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -lt 6 ]] || [[ "$#" -gt 7 ]]; then
  echo "usage: publish-profile.sh <manifest> <profile-id> <version> <bytes> <sha256> <published-at> [display-name]" >&2
  exit 1
fi

source_manifest="$1"
profile_id="$2"
profile_version="$3"
download_bytes="$4"
manifest_sha256="${5,,}"
published_at="$6"
display_name="${7:-}"
manifest_directory="/var/lib/hechao-launcher-api/manifests"
destination_manifest="${manifest_directory}/${profile_id}.json"
postgres_container="hechao-launcher-postgres"

if [[ ! -f "$source_manifest" ]] ||
   [[ ! "$profile_id" =~ ^[a-z0-9][a-z0-9._-]{1,63}$ ]] ||
   [[ ! "$profile_version" =~ ^[0-9A-Za-z._+-]{1,40}$ ]] ||
   [[ ! "$download_bytes" =~ ^[0-9]+$ ]] ||
   [[ ! "$manifest_sha256" =~ ^[0-9a-f]{64}$ ]] ||
   [[ ! "$published_at" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.+-]+Z?$ ]] ||
   [[ -n "$display_name" && ( "${#display_name}" -gt 80 || "$display_name" =~ [[:cntrl:]] ) ]]; then
  echo "invalid profile publication arguments" >&2
  exit 1
fi

actual_sha256="$(sha256sum "$source_manifest" | awk '{print $1}')"
if [[ "$actual_sha256" != "$manifest_sha256" ]]; then
  echo "manifest checksum mismatch" >&2
  exit 1
fi

release_channel_table_sql="
  SELECT to_regclass('launcher.client_profile_channels') IS NOT NULL;"
set +e
release_channel_table="$(
  docker exec "$postgres_container" sh -lc \
    'psql -X -q -U "$POSTGRES_USER" -d hechao_launcher -v ON_ERROR_STOP=1 -At -c "$1"' \
    sh "$release_channel_table_sql" 2>&1
)"
database_status=$?
set -e
if [[ "$database_status" -ne 0 ]]; then
  echo "could not inspect the profile release schema: $release_channel_table" >&2
  exit 1
fi
if [[ "$release_channel_table" == "t" ]]; then
  echo "legacy profile publishing is disabled on API 0.17 and later" >&2
  echo "import the signed manifest in the admin console, then promote Test, Gray, and Production explicitly" >&2
  exit 2
fi

install -d -o root -g hechao-api -m 0750 "$manifest_directory"
temporary_manifest="$(mktemp "${manifest_directory}/.${profile_id}.tmp.XXXXXX")"
backup_manifest=""

cleanup() {
  rm -f "$temporary_manifest"
  if [[ -n "$backup_manifest" ]]; then
    rm -f "$backup_manifest"
  fi
}
trap cleanup EXIT

install -o root -g hechao-api -m 0640 "$source_manifest" "$temporary_manifest"
if [[ -f "$destination_manifest" ]]; then
  backup_manifest="$(mktemp "${manifest_directory}/.${profile_id}.backup.XXXXXX")"
  cp --preserve=mode,ownership,timestamps "$destination_manifest" "$backup_manifest"
fi
mv -f "$temporary_manifest" "$destination_manifest"

if [[ -n "$display_name" ]]; then
  escaped_display_name="${display_name//\'/\'\'}"
  update_sql="INSERT INTO launcher.client_profiles
    (id, display_name, version, download_bytes, sha256, published_at, is_active)
VALUES
    ('${profile_id}', '${escaped_display_name}', '${profile_version}',
     ${download_bytes}, '${manifest_sha256}', '${published_at}'::timestamptz, true)
ON CONFLICT (id) DO UPDATE
SET display_name = EXCLUDED.display_name,
    version = EXCLUDED.version,
    download_bytes = EXCLUDED.download_bytes,
    sha256 = EXCLUDED.sha256,
    published_at = EXCLUDED.published_at,
    is_active = true,
    updated_at = now()
RETURNING id;"
else
  update_sql="UPDATE launcher.client_profiles
SET version = '${profile_version}',
    download_bytes = ${download_bytes},
    sha256 = '${manifest_sha256}',
    published_at = '${published_at}'::timestamptz,
    updated_at = now()
WHERE id = '${profile_id}'
RETURNING id;"
fi

set +e
updated_profile="$(
  docker exec "$postgres_container" sh -lc \
    'psql -X -q -U "$POSTGRES_USER" -d hechao_launcher -v ON_ERROR_STOP=1 -At -c "$1"' \
    sh "$update_sql" 2>&1
)"
database_status=$?
set -e

if [[ "$database_status" -ne 0 ]] ||
   [[ "$updated_profile" != "$profile_id" ]]; then
  if [[ -n "$backup_manifest" ]]; then
    mv -f "$backup_manifest" "$destination_manifest"
    backup_manifest=""
  else
    rm -f "$destination_manifest"
  fi
  echo "profile database update failed: $updated_profile" >&2
  exit 1
fi

echo "published_profile=${profile_id}"
echo "version=${profile_version}"
echo "download_bytes=${download_bytes}"
echo "manifest_sha256=${manifest_sha256}"

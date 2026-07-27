#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "hechao-verify-restored-platform-data must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 2 ]]; then
  echo "usage: hechao-verify-restored-platform-data <archive> <expected-sha256>" >&2
  exit 1
fi

archive_path="$(realpath -e "$1")"
expected_sha256="${2^^}"
restore_root="/var/backups/hechao-platform-data/restore-staging"
sub2api_database_container="${HECHAO_SUB2API_DATABASE_CONTAINER:-sub2api-postgres}"
sub2api_database_user="${HECHAO_SUB2API_DATABASE_USER:-sub2api}"
restore_database="hechao_sub2api_restore_$(date -u +%Y%m%dt%H%M%Sz)_$$"
work_root="${restore_root}/${restore_database}"
database_created=false

if [[ ! "$expected_sha256" =~ ^[0-9A-F]{64}$ ]]; then
  echo "expected SHA-256 must contain exactly 64 hexadecimal characters" >&2
  exit 1
fi

for command_name in docker jq python3 sha256sum sqlite3 tar; do
  command -v "$command_name" >/dev/null
done

cleanup() {
  local exit_code="$?"
  trap - EXIT
  if [[ "$database_created" == true ]]; then
    docker exec "$sub2api_database_container" \
      psql \
      -U "$sub2api_database_user" \
      -d postgres \
      -v ON_ERROR_STOP=1 \
      -c "DROP DATABASE IF EXISTS \"${restore_database}\" WITH (FORCE);" \
      >/dev/null 2>&1 || true
  fi
  rm -rf -- "$work_root"
  exit "$exit_code"
}
trap cleanup EXIT

actual_sha256="$(sha256sum "$archive_path" | awk '{print toupper($1)}')"
if [[ "$actual_sha256" != "$expected_sha256" ]]; then
  echo "platform data archive SHA-256 does not match" >&2
  exit 1
fi

validate_tar() {
  local tar_path="$1"
  python3 - "$tar_path" <<'PY'
import pathlib
import sys
import tarfile

archive = pathlib.Path(sys.argv[1])
with tarfile.open(archive, "r:gz") as handle:
    members = handle.getmembers()
    if not members:
        raise SystemExit("archive is empty")
    for member in members:
        path = pathlib.PurePosixPath(member.name)
        if path.is_absolute() or ".." in path.parts:
            raise SystemExit(f"unsafe archive path: {member.name}")
        if member.issym() or member.islnk() or member.isdev():
            raise SystemExit(f"unsupported archive entry: {member.name}")
PY
}

validate_tar "$archive_path"
install -d -o root -g root -m 0700 "$work_root"
tar -xzf "$archive_path" -C "$work_root"

required_files=(
  metadata.json
  manifest.sha256
  forum/forum.sqlite
  forum/source.tar.gz
  forum/environment
  sub2api/database.dump
  sub2api/configuration.tar.gz
)
for required_file in "${required_files[@]}"; do
  test -f "${work_root}/${required_file}"
done
python3 - "${work_root}/manifest.sha256" <<'PY'
import pathlib
import re
import sys

manifest = pathlib.Path(sys.argv[1])
expected_paths = {
    "metadata.json",
    "forum/forum.sqlite",
    "forum/source.tar.gz",
    "forum/environment",
    "sub2api/database.dump",
    "sub2api/configuration.tar.gz",
}
observed_paths = set()
for line in manifest.read_text(encoding="ascii").splitlines():
    match = re.fullmatch(r"([0-9a-fA-F]{64})  ([^\r\n]+)", line)
    if match is None:
        raise SystemExit("invalid manifest entry")
    observed_paths.add(match.group(2))
if observed_paths != expected_paths or len(observed_paths) != len(expected_paths):
    raise SystemExit("manifest does not contain the exact approved file set")
PY
(
  cd "$work_root"
  sha256sum -c manifest.sha256 >/dev/null
)
test "$(sqlite3 "${work_root}/forum/forum.sqlite" 'PRAGMA quick_check;')" = "ok"
validate_tar "${work_root}/forum/source.tar.gz"
validate_tar "${work_root}/sub2api/configuration.tar.gz"
docker exec -i "$sub2api_database_container" \
  pg_restore --list < "${work_root}/sub2api/database.dump" >/dev/null

docker exec "$sub2api_database_container" \
  psql \
  -U "$sub2api_database_user" \
  -d postgres \
  -v ON_ERROR_STOP=1 \
  -c "CREATE DATABASE \"${restore_database}\" TEMPLATE template0;" \
  >/dev/null
database_created=true
docker exec -i "$sub2api_database_container" \
  pg_restore \
  -U "$sub2api_database_user" \
  -d "$restore_database" \
  --exit-on-error \
  --no-owner \
  --no-privileges \
  < "${work_root}/sub2api/database.dump" \
  >/dev/null

table_count="$(
  docker exec "$sub2api_database_container" \
    psql \
    -U "$sub2api_database_user" \
    -d "$restore_database" \
    -Atc "
      SELECT COUNT(*)
      FROM pg_catalog.pg_tables
      WHERE schemaname NOT IN ('pg_catalog', 'information_schema');
    "
)"
database_bytes="$(
  docker exec "$sub2api_database_container" \
    psql \
    -U "$sub2api_database_user" \
    -d "$restore_database" \
    -Atc 'SELECT pg_database_size(current_database());'
)"
if (( table_count < 1 )) || (( database_bytes < 1 )); then
  echo "restored Sub2API database is empty" >&2
  exit 1
fi
forum_database_bytes="$(stat -c '%s' "${work_root}/forum/forum.sqlite")"

docker exec "$sub2api_database_container" \
  psql \
  -U "$sub2api_database_user" \
  -d postgres \
  -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE \"${restore_database}\" WITH (FORCE);" \
  >/dev/null
database_created=false

jq -n \
  --arg archiveSha256 "$actual_sha256" \
  --argjson forumDatabaseBytes "$forum_database_bytes" \
  --argjson sub2apiTableCount "$table_count" \
  --argjson sub2apiDatabaseBytes "$database_bytes" \
  '{
    archiveSha256: $archiveSha256,
    forumDatabaseBytes: $forumDatabaseBytes,
    sub2apiTableCount: $sub2apiTableCount,
    sub2apiDatabaseBytes: $sub2apiDatabaseBytes,
    droppedAfterVerification: true
  }'

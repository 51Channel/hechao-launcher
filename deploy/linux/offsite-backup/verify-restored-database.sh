#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "verify-restored-database.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -lt 1 || "$#" -gt 2 ]]; then
  echo "usage: verify-restored-database.sh <database-dump> [expected-sha256]" >&2
  exit 1
fi

database_dump="$1"
expected_sha256="${2:-}"
container="hechao-launcher-postgres"
database_admin="hechao_db_admin"
database_owner="hechao_api"
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
database_name="hechao_offsite_restore_$(printf '%s' "$run_id" | tr '[:upper:]-' '[:lower:]_')"
database_created=false

cleanup() {
  local exit_code="$?"
  trap - EXIT
  if [[ "$database_created" == true ]]; then
    docker exec -u postgres "$container" \
      dropdb --username="$database_admin" --if-exists "$database_name" \
      >/dev/null 2>&1 || true
  fi
  exit "$exit_code"
}
trap cleanup EXIT

test -f "$database_dump"
test -s "$database_dump"
if [[ -n "$expected_sha256" ]]; then
  actual_sha256="$(sha256sum "$database_dump" | awk '{print toupper($1)}')"
  [[ "$actual_sha256" == "${expected_sha256^^}" ]]
fi

docker exec -i -u postgres "$container" \
  pg_restore --list < "$database_dump" >/dev/null
docker exec -u postgres "$container" \
  createdb \
  --username="$database_admin" \
  --owner="$database_owner" \
  "$database_name"
database_created=true
docker exec -i -u postgres "$container" \
  pg_restore \
  --username="$database_admin" \
  --dbname="$database_name" \
  --no-owner \
  --no-privileges \
  --role="$database_owner" \
  < "$database_dump"

query() {
  docker exec -u postgres "$container" \
    psql \
    --username="$database_admin" \
    --dbname="$database_name" \
    --tuples-only \
    --no-align \
    --command="$1"
}

migration_max="$(query 'SELECT max(version) FROM launcher.schema_migrations;')"
profile_count="$(query 'SELECT count(*) FROM launcher.client_profiles;')"
server_count="$(query 'SELECT count(*) FROM launcher.servers;')"
user_count="$(query 'SELECT count(*) FROM launcher.users;')"
alert_count="$(query 'SELECT count(*) FROM launcher.operational_alerts;')"
database_bytes="$(query 'SELECT pg_database_size(current_database());')"

[[ "$migration_max" -ge 17 ]]
jq -n \
  --arg database "$database_name" \
  --argjson migrationMax "$migration_max" \
  --argjson profileCount "$profile_count" \
  --argjson serverCount "$server_count" \
  --argjson userCount "$user_count" \
  --argjson alertCount "$alert_count" \
  --argjson databaseBytes "$database_bytes" \
  '{
    database: $database,
    migrationMax: $migrationMax,
    profileCount: $profileCount,
    serverCount: $serverCount,
    userCount: $userCount,
    alertCount: $alertCount,
    databaseBytes: $databaseBytes,
    droppedAfterVerification: true
  }'

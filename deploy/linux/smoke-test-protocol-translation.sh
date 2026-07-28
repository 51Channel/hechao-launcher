#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "smoke-test-protocol-translation.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -lt 3 || "$#" -gt 4 ]]; then
  echo "usage: smoke-test-protocol-translation.sh <archive> <sha256> <database-backup> [port]" >&2
  exit 1
fi

archive="$1"
expected_sha256="${2,,}"
database_backup="$3"
port="${4:-18093}"
container="hechao-launcher-postgres"
database_admin="hechao_db_admin"
database_owner="hechao_api"
production_service="hechao-launcher-api.service"
production_environment="/etc/hechao-launcher-api/environment"
production_current="/opt/hechao-launcher-api/current"
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
database_name="hechao_protocol_smoke_$(printf '%s' "$run_id" | tr '[:upper:]-' '[:lower:]_')"
work_root="/opt/hechao-launcher-api/integration-tests/protocol-translation-${run_id}"
candidate_root="${work_root}/candidate"
environment_file="${work_root}/environment"
response_root="${work_root}/responses"
log_file="${work_root}/candidate.log"
keys_path="/tmp/hechao-protocol-smoke-keys-${run_id}"
unit_name="hechao-api-protocol-smoke-${run_id}"
base_url="http://127.0.0.1:${port}"
database_created=false
unit_started=false
work_created=false

fail() {
  echo "FAIL: $*" >&2
  return 1
}

json_value() {
  local path="$1"
  local filter="$2"
  jq -er "$filter" "$path"
}

database_scalar() {
  local database="$1"
  local sql="$2"
  docker exec -u postgres "$container" \
    psql \
    --username="$database_admin" \
    --dbname="$database" \
    --tuples-only \
    --no-align \
    --command="$sql"
}

cleanup_resources() {
  if [[ "$unit_started" == true ]]; then
    systemctl stop "${unit_name}.service" >/dev/null 2>&1 || true
    systemctl reset-failed "${unit_name}.service" >/dev/null 2>&1 || true
    unit_started=false
  fi

  if [[ "$database_created" == true ]]; then
    docker exec -u postgres "$container" \
      dropdb --username="$database_admin" --if-exists "$database_name" \
      >/dev/null 2>&1 || true
    database_created=false
  fi

  if [[ "$work_created" == true ]]; then
    case "$work_root" in
      /opt/hechao-launcher-api/integration-tests/protocol-translation-*)
        rm -rf -- "$work_root"
        ;;
      *)
        echo "refusing to remove unexpected integration-test directory" >&2
        return 1
        ;;
    esac
    work_created=false
  fi

  rm -rf -- "$keys_path"
}

cleanup() {
  local exit_code="$?"
  trap - EXIT

  if [[ "$exit_code" -ne 0 && -f "$log_file" ]]; then
    echo "Candidate log tail:" >&2
    tail -n 80 "$log_file" >&2 || true
  fi

  cleanup_resources || exit_code=1
  exit "$exit_code"
}
trap cleanup EXIT

for tool in curl docker jq openssl pg_restore python3 sha256sum ss systemctl tar; do
  command -v "$tool" >/dev/null || fail "required tool is missing: $tool"
done

[[ -f "$archive" ]] || fail "candidate archive does not exist"
[[ -f "$database_backup" ]] || fail "database backup does not exist"
[[ -f "$production_environment" ]] || fail "production environment is missing"
[[ -L "$production_current" ]] || fail "production current path is not a symlink"
[[ "$port" =~ ^[0-9]+$ ]] || fail "port must be numeric"
((port >= 1024 && port <= 65535)) || fail "port is outside the allowed range"

actual_sha256="$(sha256sum "$archive" | awk '{print $1}')"
[[ "$actual_sha256" == "$expected_sha256" ]] ||
  fail "candidate archive checksum mismatch"
pg_restore --list "$database_backup" >/dev/null

if ss -H -ltn "sport = :${port}" | grep -q .; then
  fail "candidate port ${port} is already in use"
fi

systemctl is-active --quiet "$production_service" ||
  fail "production API service is not active"
production_release_before="$(readlink -f "$production_current")"
production_state_before="$(
  systemctl show "$production_service" \
    --property=MainPID \
    --property=NRestarts \
    --property=ActiveEnterTimestampMonotonic \
    --value |
    paste -sd '|'
)"
production_database="$(
  python3 - "$production_environment" <<'PY'
import re
import sys

for line in open(sys.argv[1], "r", encoding="utf-8"):
    if not line.startswith("ConnectionStrings__LauncherDatabase="):
        continue
    match = re.search(r"(?i)(?:^|;)\s*Database\s*=\s*([^;\"']+)", line)
    if not match:
        raise SystemExit("production database name is missing")
    print(match.group(1).strip())
    break
else:
    raise SystemExit("production database connection string is missing")
PY
)"
production_migrations_before="$(
  database_scalar \
    "$production_database" \
    "SELECT count(*)::text || '|' || coalesce(max(version), 0)::text
     FROM launcher.schema_migrations;"
)"
[[ "$production_migrations_before" == *"|17" ]] ||
  fail "production database is not on the expected pre-018 migration baseline"

install -d -o root -g root -m 0755 \
  /opt/hechao-launcher-api/integration-tests \
  "$work_root" "$candidate_root" "$response_root"
work_created=true
while IFS= read -r entry; do
  case "$entry" in
    /*|..|../*|*/../*)
      fail "unsafe archive path: $entry"
      ;;
  esac
done < <(tar -tzf "$archive")
tar -xzf "$archive" -C "$candidate_root"
[[ -f "${candidate_root}/Hechao.Api" ]] || fail "candidate executable is missing"
chmod 0555 "${candidate_root}/Hechao.Api"

docker inspect "$container" >/dev/null
docker exec -u postgres "$container" \
  createdb \
  --username="$database_admin" \
  --owner="$database_owner" \
  --template=template0 \
  "$database_name"
database_created=true

cat "$database_backup" |
  docker exec -i -u postgres "$container" \
    pg_restore \
    --username="$database_admin" \
    --dbname="$database_name" \
    --clean \
    --if-exists \
    --exit-on-error \
    >/dev/null

velocity_token="$(openssl rand -hex 32)"
velocity_token_hash="$(printf '%s' "$velocity_token" | sha256sum | awk '{print $1}')"

python3 - \
  "$production_environment" \
  "$environment_file" \
  "$database_name" \
  "$port" \
  "$velocity_token_hash" \
  "$keys_path" <<'PY'
import re
import sys

source, destination, database, port, velocity_hash, keys_path = sys.argv[1:]
lines = open(source, "r", encoding="utf-8").read().splitlines()
updated = []
connection_found = False
overrides = {
    "AdminWeb__Enabled",
    "AdminWeb__DataProtectionKeyPath",
    "VelocityAuthorization__InternalTokenSha256",
}

for line in lines:
    key, separator, value = line.partition("=")
    if key in overrides:
        continue
    if key == "ConnectionStrings__LauncherDatabase":
        replaced, count = re.subn(
            r"(?i)(Database\s*=\s*)[^;\"']+",
            lambda match: match.group(1) + database,
            value,
            count=1,
        )
        if count != 1:
            raise SystemExit("could not replace database name in connection string")
        line = key + separator + replaced
        connection_found = True
    updated.append(line)

if not connection_found:
    raise SystemExit("database connection string is missing")

updated.extend(
    [
        "ASPNETCORE_ENVIRONMENT=Production",
        f"ASPNETCORE_URLS=http://127.0.0.1:{port}",
        "AdminWeb__Enabled=false",
        f"AdminWeb__DataProtectionKeyPath={keys_path}",
        f"VelocityAuthorization__InternalTokenSha256={velocity_hash}",
    ]
)
open(destination, "w", encoding="utf-8", newline="\n").write("\n".join(updated) + "\n")
PY

chmod 0600 "$environment_file"
install -d -o hechao-api -g hechao-api -m 0700 "$keys_path"
install -o hechao-api -g hechao-api -m 0600 /dev/null "$log_file"

systemd-run \
  --unit="$unit_name" \
  --uid=hechao-api \
  --gid=hechao-api \
  --property="WorkingDirectory=${candidate_root}" \
  --property="EnvironmentFile=${environment_file}" \
  --property="StandardOutput=append:${log_file}" \
  --property="StandardError=append:${log_file}" \
  --collect \
  "${candidate_root}/Hechao.Api" \
  >/dev/null
unit_started=true

ready=false
for _ in {1..40}; do
  if curl --fail --silent --show-error --max-time 2 \
    "${base_url}/readyz" \
    -o "${response_root}/ready.json"; then
    ready=true
    break
  fi
  sleep 1
done
[[ "$ready" == true ]] || fail "candidate did not become ready"
[[ "$(json_value "${response_root}/ready.json" '.version')" == "0.21.0" ]] ||
  fail "candidate reported an unexpected version"

migration_018="$(
  database_scalar \
    "$database_name" \
    "SELECT count(*)
     FROM launcher.schema_migrations
     WHERE version = 18
       AND name = 'protocol_translation_routes';"
)"
[[ "$migration_018" == "1" ]] || fail "migration 018 was not applied to the clone"

default_gate_state="$(
  database_scalar \
    "$database_name" \
    "SELECT count(*) FILTER (WHERE allow_protocol_translation)::text
         || '|' ||
         count(*) FILTER (WHERE allow_protocol_translation IS NULL)::text
     FROM launcher.servers;"
)"
[[ "$default_gate_state" == "0|0" ]] ||
  fail "migration 018 did not initialize every existing target to false"

server_value() {
  local server_id="$1"
  local column="$2"
  database_scalar \
    "$database_name" \
    "SELECT ${column} FROM launcher.servers WHERE id = '${server_id}';"
}

[[ "$(server_value lobby minecraft_version)" == "1.21.11" ]] ||
  fail "cloned lobby Minecraft version is unexpected"
[[ "$(server_value lobby loader)" == "Paper" ]] ||
  fail "cloned lobby loader is unexpected"
[[ "$(server_value pvp minecraft_version)" == "1.20.1" ]] ||
  fail "cloned PVP Minecraft version is unexpected"
[[ "$(server_value pvp loader)" == "Fabric" ]] ||
  fail "cloned PVP loader is unexpected"
[[ "$(server_value activity loader)" == "NeoForge" ]] ||
  fail "cloned Activity loader is unexpected"

database_scalar \
  "$database_name" \
  "UPDATE launcher.servers
   SET status = 'Online',
       is_visible = true,
       minimum_tier = 'Member',
       opens_at = NULL,
       closes_at = NULL
   WHERE id IN ('lobby', 'pvp', 'activity');" \
  >/dev/null

suffix="$(openssl rand -hex 4)"
user_id="$(python3 -c 'import uuid; print(uuid.uuid4())')"
minecraft_uuid="$(python3 -c 'import uuid; print(uuid.uuid4())')"
minecraft_name="Pts${suffix}"
database_scalar \
  "$database_name" \
  "INSERT INTO launcher.users
       (id, display_name, access_tier, username)
   VALUES
       ('${user_id}', 'Protocol smoke ${suffix}', 'Administrator',
        'protocol_smoke_${suffix}');
   INSERT INTO launcher.minecraft_identities
       (minecraft_uuid, user_id, minecraft_name, verified_at,
        luckperms_primary_group, luckperms_synced_at)
   VALUES
       ('${minecraft_uuid}', '${user_id}', '${minecraft_name}', now(),
        'owner', now());" \
  >/dev/null

authorize_expect() {
  local session_server_id="$1"
  local velocity_target="$2"
  local expected_reason="$3"
  local output_name="$4"
  local payload
  local status

  payload="$(
    jq -cn \
      --arg minecraftUuid "$minecraft_uuid" \
      --arg minecraftName "$minecraft_name" \
      --arg velocityTarget "$velocity_target" \
      --arg sessionServerId "$session_server_id" \
      '{
        minecraftUuid:$minecraftUuid,
        minecraftName:$minecraftName,
        velocityTarget:$velocityTarget,
        initialConnection:false,
        remoteAddress:"127.0.0.1",
        proxyInstance:"protocol-translation-smoke",
        sessionServerId:$sessionServerId
      }'
  )"
  status="$(
    curl --silent --show-error \
      --output "${response_root}/${output_name}.json" \
      --write-out '%{http_code}' \
      --header 'Content-Type: application/json' \
      --header "X-Hechao-Velocity-Token: ${velocity_token}" \
      --data "$payload" \
      "${base_url}/v1/internal/velocity/authorize"
  )"
  [[ "$status" == "200" ]] ||
    fail "${output_name}: expected HTTP 200, got ${status}"
  [[ "$(json_value "${response_root}/${output_name}.json" '.reason')" == "$expected_reason" ]] ||
    fail "${output_name}: expected ${expected_reason}"
}

authorize_expect pvp lobby MinecraftVersionMismatch pvp-to-lobby-default-off

database_scalar \
  "$database_name" \
  "UPDATE launcher.servers
   SET allow_protocol_translation = true
   WHERE id = 'lobby';" \
  >/dev/null
[[ "$(database_scalar "$database_name" \
  "SELECT string_agg(id, ',' ORDER BY id)
   FROM launcher.servers
   WHERE allow_protocol_translation;")" == "lobby" ]] ||
  fail "the translation gate was not scoped only to lobby"
authorize_expect pvp lobby Allowed pvp-to-lobby-enabled
authorize_expect lobby pvp MinecraftVersionMismatch lobby-to-pvp-target-scoped
authorize_expect pvp pvp Allowed pvp-to-pvp-same-profile

database_scalar \
  "$database_name" \
  "UPDATE launcher.servers
   SET allow_protocol_translation = true
   WHERE id = 'activity';" \
  >/dev/null
authorize_expect pvp activity ClientProfileMismatch pvp-to-activity-profile-protected

database_scalar \
  "$database_name" \
  "UPDATE launcher.servers
   SET allow_protocol_translation = false
   WHERE id IN ('lobby', 'activity');" \
  >/dev/null
authorize_expect pvp lobby MinecraftVersionMismatch pvp-to-lobby-reset-off
[[ "$(database_scalar "$database_name" \
  "SELECT count(*) FROM launcher.servers WHERE allow_protocol_translation;")" == "0" ]] ||
  fail "the cloned translation flags were not reset"

log_error_count="$(
  grep -Eic '(^|[[:space:]])(fail(ed|ure)?|error|critical|fatal|unhandled)([[:space:]:]|$)' \
    "$log_file" || true
)"
[[ "$log_error_count" == "0" ]] ||
  fail "candidate log contains error-level text"

production_release_after="$(readlink -f "$production_current")"
production_state_after="$(
  systemctl show "$production_service" \
    --property=MainPID \
    --property=NRestarts \
    --property=ActiveEnterTimestampMonotonic \
    --value |
    paste -sd '|'
)"
production_migrations_after="$(
  database_scalar \
    "$production_database" \
    "SELECT count(*)::text || '|' || coalesce(max(version), 0)::text
     FROM launcher.schema_migrations;"
)"
[[ "$production_release_after" == "$production_release_before" ]] ||
  fail "production current release changed during the smoke test"
[[ "$production_state_after" == "$production_state_before" ]] ||
  fail "production API process state changed during the smoke test"
[[ "$production_migrations_after" == "$production_migrations_before" ]] ||
  fail "production database migration state changed during the smoke test"
systemctl is-active --quiet "$production_service" ||
  fail "production API service is no longer active"

cleanup_resources
if docker exec -u postgres "$container" \
  psql \
  --username="$database_admin" \
  --dbname=postgres \
  --tuples-only \
  --no-align \
  --command="SELECT 1 FROM pg_database WHERE datname = '${database_name}';" |
  grep -q 1; then
  fail "temporary database still exists after cleanup"
fi
if systemctl is-active --quiet "${unit_name}.service"; then
  fail "temporary API unit is still active after cleanup"
fi
[[ ! -e "$work_root" ]] || fail "temporary work directory still exists after cleanup"

trap - EXIT
cat <<EOF
{
  "candidateVersion": "0.21.0",
  "migration018": "applied-on-clone",
  "existingTargetsDefaultFalse": true,
  "checks": {
    "pvpToLobbyDefaultOff": "MinecraftVersionMismatch",
    "pvpToLobbyEnabled": "Allowed",
    "targetScopedReverseRoute": "MinecraftVersionMismatch",
    "samePvpProfile": "Allowed",
    "moddedProfileProtection": "ClientProfileMismatch",
    "resetToDefaultOff": "MinecraftVersionMismatch"
  },
  "candidateLogErrorCount": 0,
  "productionReleaseUnchanged": true,
  "productionProcessUnchanged": true,
  "productionMigrationStateUnchanged": true,
  "temporaryResourcesRemoved": true
}
EOF

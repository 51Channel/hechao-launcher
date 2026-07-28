#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "manage-protocol-translation-staging.sh must run as root" >&2
  exit 1
fi

action="${1:-}"
state_root="/opt/hechao-launcher-api/integration-tests/protocol-translation-staging"
candidate_root="${state_root}/candidate"
environment_file="${state_root}/environment"
token_file="${state_root}/velocity-token"
metadata_file="${state_root}/metadata"
log_file="${state_root}/candidate.log"
keys_path="${state_root}/keys"
diagnostics_path="${state_root}/diagnostics"
database_name="hechao_protocol_translation_staging"
container="hechao-launcher-postgres"
database_admin="hechao_db_admin"
database_owner="hechao_api"
production_service="hechao-launcher-api.service"
production_environment="/etc/hechao-launcher-api/environment"
production_current="/opt/hechao-launcher-api/current"
unit_name="hechao-api-protocol-translation-staging"
port=18093
base_url="http://127.0.0.1:${port}"
prepare_complete=false
database_created=false

fail() {
  echo "FAIL: $*" >&2
  return 1
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

database_exists() {
  docker exec -u postgres "$container" \
    psql \
    --username="$database_admin" \
    --dbname=postgres \
    --tuples-only \
    --no-align \
    --command="SELECT 1 FROM pg_database WHERE datname = '${database_name}';" |
    grep -qx 1
}

production_database_name() {
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
}

production_snapshot() {
  local production_database
  local release
  local migrations
  local service_state

  systemctl is-active --quiet "$production_service" ||
    fail "production API service is not active"
  [[ -L "$production_current" ]] || fail "production current path is not a symlink"
  release="$(readlink -f "$production_current")"
  production_database="$(production_database_name)"
  migrations="$(
    database_scalar \
      "$production_database" \
      "SELECT count(*)::text || '|' || coalesce(max(version), 0)::text
       FROM launcher.schema_migrations;"
  )"
  service_state="$(
    systemctl show "$production_service" \
      --property=MainPID \
      --property=NRestarts \
      --property=ActiveEnterTimestampMonotonic \
      --value |
      paste -sd '|'
  )"
  printf '%s\n%s\n%s\n' "$release" "$migrations" "$service_state"
}

require_tools() {
  local tool
  for tool in curl docker jq openssl pg_restore python3 sha256sum ss systemctl tar; do
    command -v "$tool" >/dev/null || fail "required tool is missing: $tool"
  done
}

load_metadata() {
  [[ -f "$metadata_file" ]] || fail "staging metadata is missing"
  # The file is root-owned, mode 0600, and only contains validated generated values.
  # shellcheck disable=SC1090
  source "$metadata_file"
  [[ "${candidate_version:-}" == "0.21.0" ]] ||
    fail "staging candidate version metadata is invalid"
  [[ "${candidate_sha256:-}" =~ ^[a-f0-9]{64}$ ]] ||
    fail "staging candidate checksum metadata is invalid"
  [[ "${production_release:-}" == /opt/hechao-launcher-api/releases/* ]] ||
    fail "production release metadata is invalid"
  [[ "${production_migrations:-}" == *"|17" ]] ||
    fail "production migration metadata is invalid"
}

assert_production_baseline() {
  local snapshot
  local current_release
  local current_migrations

  load_metadata
  mapfile -t snapshot < <(production_snapshot)
  current_release="${snapshot[0]}"
  current_migrations="${snapshot[1]}"
  [[ "$current_release" == "$production_release" ]] ||
    fail "production API release changed after staging preparation"
  [[ "$current_migrations" == "$production_migrations" ]] ||
    fail "production database migration state changed after staging preparation"
}

stop_candidate() {
  if systemctl is-active --quiet "${unit_name}.service"; then
    systemctl stop "${unit_name}.service" >/dev/null
  fi
  systemctl reset-failed "${unit_name}.service" >/dev/null 2>&1 || true
}

start_candidate() {
  [[ -x "${candidate_root}/Hechao.Api" ]] ||
    fail "candidate executable is missing"
  [[ -f "$environment_file" ]] || fail "candidate environment is missing"
  if systemctl is-active --quiet "${unit_name}.service"; then
    fail "candidate API is already running"
  fi
  if ss -H -ltn "sport = :${port}" | grep -q .; then
    fail "candidate port ${port} is already in use"
  fi

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

  local ready=false
  local attempt
  for attempt in {1..40}; do
    if curl --fail --silent --show-error --max-time 2 \
      "${base_url}/readyz" \
      -o "${state_root}/ready.json"; then
      ready=true
      break
    fi
    sleep 1
  done
  [[ "$ready" == true ]] || fail "candidate did not become ready"
  [[ "$(jq -er '.version' "${state_root}/ready.json")" == "0.21.0" ]] ||
    fail "candidate reported an unexpected version"
}

assert_clone_state() {
  database_exists || fail "staging database is missing"
  [[ "$(
    database_scalar \
      "$database_name" \
      "SELECT count(*)
       FROM launcher.schema_migrations
       WHERE version = 18
         AND name = 'protocol_translation_routes';"
  )" == "1" ]] || fail "migration 018 is missing from the staging database"
  [[ "$(
    database_scalar \
      "$database_name" \
      "SELECT coalesce(string_agg(id, ',' ORDER BY id), '')
       FROM launcher.servers
       WHERE allow_protocol_translation;"
  )" == "lobby" ]] || fail "only lobby may have protocol translation enabled"
  [[ "$(
    database_scalar \
      "$database_name" \
      "SELECT count(*)
       FROM launcher.servers
       WHERE id IN ('lobby', 'pvp', 'activity')
         AND status = 'Online'
         AND is_visible
         AND opens_at IS NULL
         AND closes_at IS NULL;"
  )" == "3" ]] || fail "staging route targets are not available"
}

prepare_cleanup() {
  local exit_code="$?"
  trap - EXIT

  if [[ "$prepare_complete" != true ]]; then
    if [[ "$exit_code" -ne 0 && -f "$log_file" ]]; then
      echo "Candidate log tail:" >&2
      tail -n 80 "$log_file" >&2 || true
    fi
    stop_candidate || true
    if [[ "$database_created" == true ]] || database_exists; then
      docker exec -u postgres "$container" \
        dropdb --username="$database_admin" --if-exists "$database_name" \
        >/dev/null 2>&1 || true
    fi
    case "$state_root" in
      /opt/hechao-launcher-api/integration-tests/protocol-translation-staging)
        rm -rf -- "$state_root"
        ;;
      *)
        echo "refusing to remove unexpected staging state path" >&2
        exit_code=1
        ;;
    esac
  fi
  exit "$exit_code"
}

prepare() {
  [[ "$#" -eq 3 ]] ||
    fail "usage: manage-protocol-translation-staging.sh prepare <archive> <sha256> <database-backup>"
  local archive="$1"
  local expected_sha256="${2,,}"
  local database_backup="$3"
  local actual_sha256
  local backup_sha256
  local velocity_token
  local velocity_token_hash
  local snapshot_before
  local snapshot_after

  [[ ! -e "$state_root" ]] || fail "staging state already exists"
  ! database_exists || fail "staging database already exists"
  ! systemctl is-active --quiet "${unit_name}.service" ||
    fail "staging API unit is already active"
  [[ -f "$archive" ]] || fail "candidate archive does not exist"
  [[ -f "$database_backup" ]] || fail "database backup does not exist"
  [[ "$expected_sha256" =~ ^[a-f0-9]{64}$ ]] ||
    fail "candidate checksum must be a SHA-256 digest"

  actual_sha256="$(sha256sum "$archive" | awk '{print $1}')"
  [[ "$actual_sha256" == "$expected_sha256" ]] ||
    fail "candidate archive checksum mismatch"
  pg_restore --list "$database_backup" >/dev/null
  backup_sha256="$(sha256sum "$database_backup" | awk '{print $1}')"
  mapfile -t snapshot_before < <(production_snapshot)
  [[ "${snapshot_before[1]}" == *"|17" ]] ||
    fail "production database is not on the expected pre-018 baseline"

  trap prepare_cleanup EXIT
  install -d -o root -g root -m 0755 \
    "$state_root" "$candidate_root"
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
  printf '%s' "$velocity_token" > "$token_file"
  chmod 0600 "$token_file"

  python3 - \
    "$production_environment" \
    "$environment_file" \
    "$database_name" \
    "$port" \
    "$velocity_token_hash" \
    "$keys_path" \
    "$diagnostics_path" <<'PY'
import re
import sys

(
    source,
    destination,
    database,
    port,
    velocity_hash,
    keys_path,
    diagnostics_path,
) = sys.argv[1:]
lines = open(source, "r", encoding="utf-8").read().splitlines()
updated = []
connection_found = False
overrides = {
    "ASPNETCORE_URLS",
    "AdminWeb__Enabled",
    "AdminWeb__DataProtectionKeyPath",
    "DiagnosticUploads__StorageRoot",
    "ForumSessionRevocation__Enabled",
    "OperationalAlerts__Enabled",
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
        f"DiagnosticUploads__StorageRoot={diagnostics_path}",
        "ForumSessionRevocation__Enabled=false",
        "OperationalAlerts__Enabled=false",
        f"VelocityAuthorization__InternalTokenSha256={velocity_hash}",
    ]
)
open(destination, "w", encoding="utf-8", newline="\n").write("\n".join(updated) + "\n")
PY

  chmod 0600 "$environment_file"
  install -d -o hechao-api -g hechao-api -m 0700 "$keys_path" "$diagnostics_path"

  start_candidate
  stop_candidate

  [[ "$(
    database_scalar \
      "$database_name" \
      "SELECT count(*) FILTER (WHERE allow_protocol_translation)::text
           || '|' ||
           count(*) FILTER (WHERE allow_protocol_translation IS NULL)::text
       FROM launcher.servers;"
  )" == "0|0" ]] ||
    fail "migration 018 did not default every existing target to false"
  [[ "$(database_scalar "$database_name" \
    "SELECT minecraft_version || '|' || loader
     FROM launcher.servers WHERE id = 'lobby';")" == "1.21.11|Paper" ]] ||
    fail "cloned lobby profile is unexpected"
  [[ "$(database_scalar "$database_name" \
    "SELECT minecraft_version || '|' || loader
     FROM launcher.servers WHERE id = 'pvp';")" == "1.20.1|Fabric" ]] ||
    fail "cloned PVP profile is unexpected"

  database_scalar \
    "$database_name" \
    "UPDATE launcher.servers
     SET status = 'Online',
         is_visible = true,
         opens_at = NULL,
         closes_at = NULL,
         allow_protocol_translation = (id = 'lobby')
     WHERE id IN ('lobby', 'pvp', 'activity');
     UPDATE launcher.servers
     SET allow_protocol_translation = false
     WHERE id NOT IN ('lobby');" \
    >/dev/null

  cat > "$metadata_file" <<EOF
candidate_version='0.21.0'
candidate_sha256='${actual_sha256}'
backup_sha256='${backup_sha256}'
production_release='${snapshot_before[0]}'
production_migrations='${snapshot_before[1]}'
EOF
  chmod 0600 "$metadata_file"
  assert_clone_state

  mapfile -t snapshot_after < <(production_snapshot)
  [[ "${snapshot_after[0]}" == "${snapshot_before[0]}" ]] ||
    fail "production release changed during staging preparation"
  [[ "${snapshot_after[1]}" == "${snapshot_before[1]}" ]] ||
    fail "production migration state changed during staging preparation"
  [[ "${snapshot_after[2]}" == "${snapshot_before[2]}" ]] ||
    fail "production API process state changed during staging preparation"

  prepare_complete=true
  trap - EXIT
  cat <<EOF
{
  "prepared": true,
  "candidateVersion": "0.21.0",
  "listener": "127.0.0.1:${port}",
  "candidateRunning": false,
  "migration018": true,
  "translationTarget": "lobby",
  "productionUnchanged": true
}
EOF
}

status() {
  assert_production_baseline
  assert_clone_state

  local active=false
  local ready=false
  local listener_count
  local non_loopback_count
  local log_error_count
  if systemctl is-active --quiet "${unit_name}.service"; then
    active=true
    if curl --fail --silent --show-error --max-time 2 \
      "${base_url}/readyz" \
      -o "${state_root}/ready.json"; then
      [[ "$(jq -er '.version' "${state_root}/ready.json")" == "0.21.0" ]] ||
        fail "running candidate reported an unexpected version"
      ready=true
    fi
  fi
  listener_count="$(ss -H -ltn "sport = :${port}" | wc -l)"
  non_loopback_count="$(
    ss -H -ltn "sport = :${port}" |
      awk '$4 !~ /^127\.0\.0\.1:/ { count++ } END { print count + 0 }'
  )"
  [[ "$non_loopback_count" == "0" ]] ||
    fail "candidate API has a non-loopback listener"
  if [[ "$active" == true ]]; then
    [[ "$listener_count" == "1" ]] || fail "candidate API listener is missing"
    [[ "$ready" == true ]] || fail "candidate API is not ready"
  else
    [[ "$listener_count" == "0" ]] ||
      fail "candidate port is listening while its unit is inactive"
  fi
  log_error_count="$(
    grep -Eic '(^|[[:space:]])(fail(ed|ure)?|error|critical|fatal|unhandled)([[:space:]:]|$)' \
      "$log_file" 2>/dev/null || true
  )"

  cat <<EOF
{
  "prepared": true,
  "candidateVersion": "0.21.0",
  "running": ${active},
  "ready": ${ready},
  "listener": "127.0.0.1:${port}",
  "listenerCount": ${listener_count},
  "translationTarget": "lobby",
  "candidateLogErrorCount": ${log_error_count},
  "productionReleaseUnchanged": true,
  "productionMigrationStateUnchanged": true
}
EOF
}

issue_grant() {
  assert_production_baseline
  assert_clone_state
  systemctl is-active --quiet "${unit_name}.service" ||
    fail "candidate API must be running before issuing a grant"

  local eligible_count
  local grant_id
  local expires_at
  eligible_count="$(
    database_scalar \
      "$database_name" \
      "SELECT count(*)
       FROM launcher.minecraft_identities identity
       JOIN launcher.users account ON account.id = identity.user_id
       WHERE NOT account.is_disabled
         AND NOT EXISTS (
           SELECT 1
           FROM launcher.minecraft_identity_bans ban
           WHERE ban.minecraft_uuid = identity.minecraft_uuid
             AND ban.revoked_at IS NULL
             AND (ban.expires_at IS NULL OR ban.expires_at > now())
         );"
  )"
  [[ "$eligible_count" == "1" ]] ||
    fail "expected exactly one eligible linked Minecraft identity in the backup clone"

  grant_id="$(python3 -c 'import uuid; print(uuid.uuid4())')"
  expires_at="$(
    database_scalar \
      "$database_name" \
      "WITH chosen AS (
         SELECT identity.minecraft_uuid, identity.user_id
         FROM launcher.minecraft_identities identity
         JOIN launcher.users account ON account.id = identity.user_id
         WHERE NOT account.is_disabled
           AND NOT EXISTS (
             SELECT 1
             FROM launcher.minecraft_identity_bans ban
             WHERE ban.minecraft_uuid = identity.minecraft_uuid
               AND ban.revoked_at IS NULL
               AND (ban.expires_at IS NULL OR ban.expires_at > now())
           )
         LIMIT 1
       ),
       revoked AS (
         UPDATE launcher.velocity_launch_grants grant_row
         SET revoked_at = now()
         FROM chosen
         WHERE grant_row.user_id = chosen.user_id
           AND grant_row.consumed_at IS NULL
           AND grant_row.revoked_at IS NULL
         RETURNING grant_row.id
       ),
       overrides AS (
         INSERT INTO launcher.server_access_overrides
             (user_id, server_id, decision, reason, created_by, revision, updated_at)
         SELECT chosen.user_id,
                target.server_id,
                'Allow',
                'isolated protocol translation real-session staging',
                chosen.user_id,
                1,
                now()
         FROM chosen
         CROSS JOIN (VALUES ('pvp'), ('lobby')) AS target(server_id)
         ON CONFLICT (user_id, server_id) DO UPDATE
         SET decision = 'Allow',
             reason = EXCLUDED.reason,
             expires_at = NULL,
             created_by = EXCLUDED.created_by,
             revision = launcher.server_access_overrides.revision + 1,
             updated_at = now()
         RETURNING user_id
       ),
       inserted AS (
         INSERT INTO launcher.velocity_launch_grants
             (id, user_id, minecraft_uuid, requested_server_id,
              source_ip, created_at, expires_at)
         SELECT '${grant_id}'::uuid,
                chosen.user_id,
                chosen.minecraft_uuid,
                'pvp',
                NULL,
                now(),
                now() + interval '15 minutes'
         FROM chosen
         RETURNING expires_at
       )
       SELECT to_char(expires_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"')
       FROM inserted;"
  )"
  [[ -n "$expires_at" ]] || fail "staging launch grant was not created"

  cat <<EOF
{
  "grantCreated": true,
  "targetServer": "pvp",
  "eligibleIdentityCount": 1,
  "expiresAtUtc": "${expires_at}",
  "identityDisclosed": false
}
EOF
}

remove_staging() {
  [[ "${1:-}" == "--confirm-remove" ]] ||
    fail "remove requires --confirm-remove"
  if [[ -e "$state_root" ]]; then
    assert_production_baseline
  fi
  stop_candidate
  if database_exists; then
    docker exec -u postgres "$container" \
      dropdb --username="$database_admin" --if-exists "$database_name" \
      >/dev/null
  fi
  case "$state_root" in
    /opt/hechao-launcher-api/integration-tests/protocol-translation-staging)
      rm -rf -- "$state_root"
      ;;
    *)
      fail "refusing to remove unexpected staging state path"
      ;;
  esac
  ! database_exists || fail "staging database still exists after removal"
  ! systemctl is-active --quiet "${unit_name}.service" ||
    fail "staging API unit is still active after removal"
  cat <<EOF
{
  "removed": true,
  "productionUnchanged": true
}
EOF
}

require_tools
case "$action" in
  prepare)
    shift
    prepare "$@"
    ;;
  start)
    [[ "$#" -eq 1 ]] || fail "start does not accept additional arguments"
    assert_production_baseline
    assert_clone_state
    start_candidate
    status
    ;;
  status)
    [[ "$#" -eq 1 ]] || fail "status does not accept additional arguments"
    status
    ;;
  issue-grant)
    [[ "$#" -eq 1 ]] || fail "issue-grant does not accept additional arguments"
    issue_grant
    ;;
  stop)
    [[ "$#" -eq 1 ]] || fail "stop does not accept additional arguments"
    assert_production_baseline
    stop_candidate
    status
    ;;
  remove)
    shift
    remove_staging "$@"
    ;;
  *)
    echo "usage: manage-protocol-translation-staging.sh {prepare|start|status|issue-grant|stop|remove}" >&2
    exit 1
    ;;
esac

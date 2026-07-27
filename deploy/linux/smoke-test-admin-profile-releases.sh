#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "smoke-test-admin-profile-releases.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -lt 5 || "$#" -gt 6 ]]; then
  echo "usage: smoke-test-admin-profile-releases.sh <archive> <sha256> <database-backup> <older-manifest> <newer-manifest> [port]" >&2
  exit 1
fi

archive="$1"
expected_sha256="${2,,}"
database_backup="$3"
older_manifest="$4"
newer_manifest="$5"
port="${6:-18091}"
profile_id="activity-neoforge-1.21.11"
container="hechao-launcher-postgres"
database_admin="hechao_db_admin"
database_owner="hechao_api"
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
database_name="hechao_profile_smoke_$(printf '%s' "$run_id" | tr '[:upper:]-' '[:lower:]_')"
work_root="/opt/hechao-launcher-api/integration-tests/profile-releases-${run_id}"
candidate_root="${work_root}/candidate"
response_root="${work_root}/responses"
environment_file="${work_root}/environment"
log_file="${work_root}/candidate.log"
manifest_root="${work_root}/manifests"
keys_root="${work_root}/keys"
diagnostics_root="${work_root}/diagnostics"
unit_name="hechao-api-profile-smoke-${run_id}"
base_url="http://127.0.0.1:${port}"
forwarded_proto_header="X-Forwarded-Proto: https"
database_created=false
unit_started=false

fail() {
  echo "FAIL: $*" >&2
  return 1
}

assert_status() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [[ "$actual" == "$expected" ]] ||
    fail "${label}: expected HTTP ${expected}, got ${actual}"
}

json_value() {
    local path="$1"
    shift
    jq -er "$@" "$path"
}

cleanup() {
  local exit_code="$?"
  trap - EXIT

  if [[ "$unit_started" == true ]]; then
    systemctl stop "${unit_name}.service" >/dev/null 2>&1 || true
    systemctl reset-failed "${unit_name}.service" >/dev/null 2>&1 || true
  fi
  if [[ "$database_created" == true ]]; then
    docker exec -u postgres "$container" \
      dropdb --username="$database_admin" --if-exists "$database_name" \
      >/dev/null 2>&1 || true
  fi
  if [[ "$exit_code" -ne 0 && -f "$log_file" ]]; then
    echo "Candidate log tail:" >&2
    tail -n 100 "$log_file" >&2 || true
  fi

  case "$work_root" in
    /opt/hechao-launcher-api/integration-tests/profile-releases-*)
      rm -rf -- "$work_root"
      ;;
    *)
      echo "refusing to remove unexpected integration-test directory" >&2
      exit_code=1
      ;;
  esac
  exit "$exit_code"
}
trap cleanup EXIT

for tool in curl docker jq openssl pg_restore python3 sed sha256sum systemctl tar; do
  command -v "$tool" >/dev/null || fail "required tool is missing: $tool"
done

curl() {
  command curl --header 'Host: admin.hechao.world' "$@"
}

for path in "$archive" "$database_backup" "$older_manifest" "$newer_manifest"; do
  [[ -f "$path" ]] || fail "required input does not exist: $path"
done
[[ "$port" =~ ^[0-9]+$ ]] || fail "port must be numeric"
((port >= 1024 && port <= 65535)) || fail "port is outside the allowed range"

actual_sha256="$(sha256sum "$archive" | awk '{print $1}')"
[[ "$actual_sha256" == "$expected_sha256" ]] ||
  fail "candidate archive checksum mismatch"
older_sha256="$(sha256sum "$older_manifest" | awk '{print $1}')"
newer_sha256="$(sha256sum "$newer_manifest" | awk '{print $1}')"
[[ "$older_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "older manifest checksum is invalid"
[[ "$newer_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "newer manifest checksum is invalid"
[[ "$older_sha256" != "$newer_sha256" ]] ||
  fail "release manifests must be distinct"
pg_restore --list "$database_backup" >/dev/null

if ss -H -ltn "sport = :${port}" | grep -q .; then
  fail "candidate port ${port} is already in use"
fi

install -d -o root -g root -m 0755 \
  /opt/hechao-launcher-api/integration-tests \
  "$work_root" "$candidate_root" "$response_root"
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

forum_token="$(openssl rand -hex 32)"
forum_token_hash="$(printf '%s' "$forum_token" | sha256sum | awk '{print $1}')"
velocity_token_hash="$(openssl rand -hex 32 | sha256sum | awk '{print $1}')"
sync_token_hash="$(openssl rand -hex 32 | sha256sum | awk '{print $1}')"
heartbeat_token="$(openssl rand -hex 32)"
heartbeat_token_hash="$(
  printf '%s' "$heartbeat_token" | sha256sum | awk '{print $1}'
)"
alert_token="$(openssl rand -hex 32)"
alert_token_hash="$(
  printf '%s' "$alert_token" | sha256sum | awk '{print $1}'
)"

python3 - \
  /etc/hechao-launcher-api/environment \
  "$environment_file" \
  "$database_name" \
  "$port" \
  "$forum_token_hash" \
  "$velocity_token_hash" \
  "$sync_token_hash" \
  "$heartbeat_token_hash" \
  "$alert_token_hash" \
  "$keys_root" \
  "$diagnostics_root" \
  "$manifest_root" <<'PY'
import re
import sys

(
    source,
    destination,
    database,
    port,
    forum_hash,
    velocity_hash,
    sync_hash,
    heartbeat_hash,
    alert_hash,
    keys_path,
    diagnostics_path,
    manifest_path,
) = sys.argv[1:]
lines = open(source, "r", encoding="utf-8").read().splitlines()
updated = []
connection_found = False
overrides = {
    "AdminWeb__Enabled",
    "AdminWeb__PublicBaseUrl",
    "AdminWeb__DataProtectionKeyPath",
    "DiagnosticUploads__StorageRoot",
    "Distribution__ManifestDirectory",
    "ForumAccountBridge__InternalTokenSha256",
    "VelocityAuthorization__InternalTokenSha256",
    "Authentication__InternalSyncTokenSha256",
    "ServerHeartbeats__InternalTokenSha256",
    "OperationalAlerts__InternalTokenSha256",
}

for line in lines:
    key, separator, value = line.partition("=")
    if key in overrides or key.startswith("ForumSessionRevocation__"):
        continue
    if key == "ConnectionStrings__LauncherDatabase":
        value, count = re.subn(
            r"(?i)(Database\s*=\s*)[^;\"']+",
            lambda match: match.group(1) + database,
            value,
            count=1,
        )
        if count != 1:
            raise SystemExit("could not replace database name")
        line = key + separator + value
        connection_found = True
    updated.append(line)

if not connection_found:
    raise SystemExit("database connection string is missing")

updated.extend(
    [
        "ASPNETCORE_ENVIRONMENT=Production",
        f"ASPNETCORE_URLS=http://127.0.0.1:{port}",
        "AdminWeb__Enabled=true",
        "AdminWeb__PublicBaseUrl=https://admin.hechao.world",
        f"AdminWeb__DataProtectionKeyPath={keys_path}",
        f"DiagnosticUploads__StorageRoot={diagnostics_path}",
        f"Distribution__ManifestDirectory={manifest_path}",
        f"ForumAccountBridge__InternalTokenSha256={forum_hash}",
        f"VelocityAuthorization__InternalTokenSha256={velocity_hash}",
        f"Authentication__InternalSyncTokenSha256={sync_hash}",
        f"ServerHeartbeats__InternalTokenSha256={heartbeat_hash}",
        f"OperationalAlerts__InternalTokenSha256={alert_hash}",
        "ForumSessionRevocation__Enabled=false",
    ]
)
open(destination, "w", encoding="utf-8", newline="\n").write(
    "\n".join(updated) + "\n"
)
PY

chmod 0600 "$environment_file"
install -d -o hechao-api -g hechao-api -m 0700 \
  "$keys_root" "$diagnostics_root" "$manifest_root"
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
[[ "$(json_value "${response_root}/ready.json" '.version')" == "0.20.0" ]] ||
  fail "candidate reported an unexpected version"

migration_count="$(
  docker exec -u postgres "$container" \
    psql --username="$database_admin" --dbname="$database_name" \
    --tuples-only --no-align \
    --command="SELECT count(*) FROM launcher.schema_migrations WHERE version IN (15, 16, 17);"
)"
[[ "$migration_count" == "3" ]] || fail "migrations 15 through 17 were not applied"

for asset in index.html admin.js admin.css; do
  status="$(
    curl --silent --show-error \
      --output "${response_root}/${asset}" \
      --write-out '%{http_code}' \
      --header "$forwarded_proto_header" \
      "${base_url}/admin/${asset}"
  )"
  assert_status 200 "$status" "admin static asset ${asset}"
done
grep -Fq 'profile-channel-list' "${response_root}/index.html" ||
  fail "admin page does not contain release channels"
grep -Fq 'submitProfilePause' "${response_root}/admin.js" ||
  fail "admin script does not contain release pause workflow"
grep -Fq 'telemetry-section' "${response_root}/index.html" ||
  fail "admin page does not contain launcher telemetry"
grep -Fq 'renderTelemetry' "${response_root}/admin.js" ||
  fail "admin script does not contain launcher telemetry rendering"
grep -Fq 'runtime-section' "${response_root}/index.html" ||
  fail "admin page does not contain server runtime status"
grep -Fq 'renderRuntime' "${response_root}/admin.js" ||
  fail "admin script does not contain server runtime rendering"
grep -Fq 'alerts-section' "${response_root}/index.html" ||
  fail "admin page does not contain operational alerts"
grep -Fq 'renderAlerts' "${response_root}/admin.js" ||
  fail "admin script does not contain operational alert rendering"

suffix="$(openssl rand -hex 4)"
admin_username="profadm${suffix}"
admin_display="Profile Smoke ${suffix}"
admin_email="${admin_username}@example.invalid"
admin_password="ProfileA9$(openssl rand -hex 10)"
admin_uuid="$(python3 -c 'import uuid; print(uuid.uuid4())')"
admin_minecraft_name="Prf${suffix}"
register_payload="$(
  jq -cn \
    --arg username "$admin_username" \
    --arg displayName "$admin_display" \
    --arg email "$admin_email" \
    --arg password "$admin_password" \
    '{username:$username,displayName:$displayName,email:$email,password:$password}'
)"
register_status="$(
  curl --silent --show-error \
    --output "${response_root}/admin-register.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "X-Hechao-Forum-Token: ${forum_token}" \
    --data "$register_payload" \
    "${base_url}/v1/internal/forum/accounts/register"
)"
assert_status 201 "$register_status" "admin account registration"
admin_user_id="$(json_value "${response_root}/admin-register.json" '.userId')"

docker exec -u postgres "$container" \
  psql --username="$database_admin" --dbname="$database_name" \
  --set=ON_ERROR_STOP=1 \
  --command="
    UPDATE launcher.users
    SET access_tier = 'Administrator', updated_at = now()
    WHERE id = '${admin_user_id}';
    INSERT INTO launcher.minecraft_identities
        (minecraft_uuid, user_id, minecraft_name, verified_at, updated_at,
         luckperms_primary_group, luckperms_synced_at)
    VALUES
        ('${admin_uuid}', '${admin_user_id}', '${admin_minecraft_name}',
         now(), now(), 'owner', now());" \
  >/dev/null

login_payload="$(
  jq -cn \
    --arg usernameOrEmail "$admin_username" \
    --arg password "$admin_password" \
    '{usernameOrEmail:$usernameOrEmail,password:$password}'
)"
login_status="$(
  curl --silent --show-error \
    --output "${response_root}/admin-login.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --data "$login_payload" \
    "${base_url}/v1/auth/login"
)"
assert_status 200 "$login_status" "admin launcher login"
admin_access_token="$(json_value "${response_root}/admin-login.json" '.accessToken')"

telemetry_now="$(date --utc +'%Y-%m-%dT%H:%M:%SZ')"
telemetry_event_a="$(python3 -c 'import uuid; print(uuid.uuid4())')"
telemetry_event_b="$(python3 -c 'import uuid; print(uuid.uuid4())')"
telemetry_payload="$(
  jq -cn \
    --arg eventA "$telemetry_event_a" \
    --arg eventB "$telemetry_event_b" \
    --arg occurredAt "$telemetry_now" \
    '{
      events:[
        {
          eventId:$eventA,
          type:"Install",
          outcome:"Failure",
          failureCode:"NetworkUnavailable",
          launcherVersion:"0.11.13",
          occurredAt:$occurredAt,
          profileId:"activity-neoforge-1.21.11",
          profileVersion:"1.0.10",
          durationMilliseconds:1200,
          bytes:4096
        },
        {
          eventId:$eventB,
          type:"Launch",
          outcome:"Success",
          failureCode:"None",
          launcherVersion:"0.11.13",
          occurredAt:$occurredAt,
          profileId:"activity-neoforge-1.21.11",
          profileVersion:"1.0.10",
          durationMilliseconds:800,
          bytes:null
        }
      ]
    }'
)"
for attempt in first duplicate; do
  telemetry_status="$(
    curl --silent --show-error \
      --output "${response_root}/telemetry-${attempt}.json" \
      --write-out '%{http_code}' \
      --header "Authorization: Bearer ${admin_access_token}" \
      --header 'Content-Type: application/json' \
      --data "$telemetry_payload" \
      "${base_url}/v1/telemetry/events"
  )"
  assert_status 200 "$telemetry_status" "launcher telemetry ${attempt} submission"
done
[[ "$(json_value "${response_root}/telemetry-first.json" '.accepted')" == "2" ]] ||
  fail "first telemetry submission was not accepted"
[[ "$(json_value "${response_root}/telemetry-first.json" '.duplicates')" == "0" ]] ||
  fail "first telemetry submission unexpectedly contained duplicates"
[[ "$(json_value "${response_root}/telemetry-duplicate.json" '.accepted')" == "0" ]] ||
  fail "duplicate telemetry submission inserted rows"
[[ "$(json_value "${response_root}/telemetry-duplicate.json" '.duplicates')" == "2" ]] ||
  fail "duplicate telemetry submission was not idempotent"

ticket_status="$(
  curl --silent --show-error \
    --output "${response_root}/ticket.json" \
    --write-out '%{http_code}' \
    --request POST \
    --header "Authorization: Bearer ${admin_access_token}" \
    --header 'Content-Type: application/json' \
    --data '{}' \
    "${base_url}/v1/admin-auth/tickets"
)"
assert_status 200 "$ticket_status" "admin browser ticket"
browser_url="$(json_value "${response_root}/ticket.json" '.browserUrl')"
ticket="$(
  python3 - "$browser_url" <<'PY'
import sys
import urllib.parse

values = urllib.parse.parse_qs(
    urllib.parse.urlparse(sys.argv[1]).fragment
).get("ticket", [])
if len(values) != 1:
    raise SystemExit("ticket fragment is missing")
print(values[0])
PY
)"
redeem_payload="$(jq -cn --arg ticket "$ticket" '{ticket:$ticket}')"
redeem_status="$(
  curl --silent --show-error \
    --dump-header "${response_root}/redeem.headers" \
    --output "${response_root}/redeem.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --data "$redeem_payload" \
    "${base_url}/v1/admin-auth/redeem"
)"
assert_status 200 "$redeem_status" "admin browser ticket redemption"
admin_cookie="$(
  sed -n \
    's/^set-cookie: __Host-HechaoAdmin=\([^;]*\).*/\1/ip' \
    "${response_root}/redeem.headers" |
    head -n 1 |
    tr -d '\r'
)"
[[ -n "$admin_cookie" ]] || fail "admin session cookie is missing"

csrf_status="$(
  curl --silent --show-error \
    --dump-header "${response_root}/csrf.headers" \
    --output "${response_root}/csrf.json" \
    --write-out '%{http_code}' \
    --header "$forwarded_proto_header" \
    --header "Cookie: __Host-HechaoAdmin=${admin_cookie}" \
    "${base_url}/v1/admin-auth/csrf"
)"
assert_status 200 "$csrf_status" "CSRF token"
csrf_cookie="$(
  sed -n \
    's/^set-cookie: __Host-HechaoAdminCsrf=\([^;]*\).*/\1/ip' \
    "${response_root}/csrf.headers" |
    head -n 1 |
    tr -d '\r'
)"
csrf_token="$(json_value "${response_root}/csrf.json" '.requestToken')"
[[ -n "$csrf_cookie" && -n "$csrf_token" ]] ||
  fail "CSRF token pair is missing"
admin_cookie_header="__Host-HechaoAdmin=${admin_cookie}; __Host-HechaoAdminCsrf=${csrf_cookie}"

enrollment_status="$(
  curl --silent --show-error \
    --output "${response_root}/mfa-enrollment.json" \
    --write-out '%{http_code}' \
    --request POST \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data '{}' \
    "${base_url}/v1/admin-auth/mfa/enrollment"
)"
assert_status 200 "$enrollment_status" "MFA enrollment"
mfa_secret="$(json_value "${response_root}/mfa-enrollment.json" '.secretKey')"
mfa_code="$(
  python3 - "$mfa_secret" <<'PY'
import base64
import hashlib
import hmac
import struct
import sys
import time

secret = sys.argv[1].strip().replace(" ", "").upper()
secret += "=" * ((8 - len(secret) % 8) % 8)
key = base64.b32decode(secret)
window = int(time.time()) // 30
digest = hmac.new(key, struct.pack(">Q", window), hashlib.sha1).digest()
offset = digest[-1] & 0x0F
value = struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF
print(f"{value % 1_000_000:06d}")
PY
)"
mfa_payload="$(jq -cn --arg code "$mfa_code" '{code:$code}')"
mfa_status="$(
  curl --silent --show-error \
    --output "${response_root}/mfa-confirm.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$mfa_payload" \
    "${base_url}/v1/admin-auth/mfa/enrollment/confirm"
)"
assert_status 200 "$mfa_status" "MFA enrollment confirmation"
[[ "$(json_value "${response_root}/mfa-confirm.json" '.verified')" == "true" ]] ||
  fail "MFA session was not verified"

admin_get() {
  local path="$1"
  local output="$2"
  curl --silent --show-error \
    --output "$output" \
    --write-out '%{http_code}' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    "${base_url}${path}"
}

admin_json_write() {
  local method="$1"
  local path="$2"
  local payload="$3"
  local output="$4"
  curl --silent --show-error \
    --output "$output" \
    --write-out '%{http_code}' \
    --request "$method" \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$payload" \
    "${base_url}${path}"
}

telemetry_summary_status="$(
  admin_get \
    "/v1/admin/telemetry/summary?hours=24" \
    "${response_root}/telemetry-summary.json"
)"
assert_status 200 "$telemetry_summary_status" "launcher telemetry summary"
[[ "$(json_value "${response_root}/telemetry-summary.json" '.downloads.attempts')" == "1" ]] ||
  fail "telemetry summary did not include the download attempt"
[[ "$(json_value "${response_root}/telemetry-summary.json" '.downloads.failed')" == "1" ]] ||
  fail "telemetry summary did not include the download failure"
[[ "$(json_value "${response_root}/telemetry-summary.json" '.launches.succeeded')" == "1" ]] ||
  fail "telemetry summary did not include the successful launch"
[[ "$(json_value "${response_root}/telemetry-summary.json" '.launcherVersions[0].launcherVersion')" == "0.11.13" ]] ||
  fail "telemetry summary did not include launcher version usage"
[[ "$(json_value "${response_root}/telemetry-summary.json" '.failures[0].failureCode')" == "NetworkUnavailable" ]] ||
  fail "telemetry summary did not include the fixed failure code"

heartbeat_now="$(date --utc +'%Y-%m-%dT%H:%M:%SZ')"
heartbeat_started_at="$(
  date --utc --date='3 hours ago' +'%Y-%m-%dT%H:%M:%SZ'
)"
heartbeat_payload="$(
  jq -cn \
    --arg capturedAt "$heartbeat_now" \
    --arg processStartedAt "$heartbeat_started_at" \
    '{
      capturedAt:$capturedAt,
      collectorInstance:"smoke-runtime-collector",
      servers:[
        {
          velocityTarget:"lobby",
          online:true,
          onlinePlayers:12,
          maxPlayers:300,
          softwareVersion:"Paper 1.21.11",
          protocolVersion:774,
          processWorkingSetBytes:4294967296,
          processPrivateBytes:5368709120,
          processCpuPercent:37.5,
          processStartedAt:$processStartedAt,
          diskFreeBytes:214748364800,
          diskTotalBytes:536870912000,
          tps1m:19.98,
          tps5m:19.97,
          tps15m:19.96,
          msptAverage:18.4,
          gcCollectionTimeMilliseconds:12345,
          metricsCapturedAt:$capturedAt,
          issues:["DiskProbeFailed"]
        }
      ]
    }'
)"
for attempt in first duplicate; do
  heartbeat_status="$(
    curl --silent --show-error \
      --output "${response_root}/heartbeat-${attempt}.json" \
      --write-out '%{http_code}' \
      --header 'Content-Type: application/json' \
      --header "X-Hechao-Heartbeat-Token: ${heartbeat_token}" \
      --data "$heartbeat_payload" \
      "${base_url}/v1/internal/server-heartbeats"
  )"
  assert_status 200 "$heartbeat_status" "server runtime heartbeat ${attempt}"
done
runtime_sample_count="$(
  docker exec -u postgres "$container" \
    psql --username="$database_admin" --dbname="$database_name" \
    --tuples-only --no-align \
    --command="
      SELECT count(*)
      FROM launcher.server_runtime_samples
      WHERE velocity_target = 'lobby'
        AND collector_instance = 'smoke-runtime-collector';"
)"
[[ "$runtime_sample_count" == "1" ]] ||
  fail "duplicate heartbeat inserted more than one runtime sample"

runtime_summary_status="$(
  admin_get \
    "/v1/admin/server-runtime/summary" \
    "${response_root}/runtime-summary.json"
)"
assert_status 200 "$runtime_summary_status" "server runtime summary"
[[ "$(json_value "${response_root}/runtime-summary.json" '.targets[] | select(.velocityTarget == "lobby") | .isFresh')" == "true" ]] ||
  fail "runtime summary did not mark lobby heartbeat fresh"
[[ "$(json_value "${response_root}/runtime-summary.json" '.targets[] | select(.velocityTarget == "lobby") | .processWorkingSetBytes')" == "4294967296" ]] ||
  fail "runtime summary did not include process memory"
[[ "$(json_value "${response_root}/runtime-summary.json" '.targets[] | select(.velocityTarget == "lobby") | .tps1m')" == "19.98" ]] ||
  fail "runtime summary did not include TPS"
[[ "$(json_value "${response_root}/runtime-summary.json" '.issues[] | select(.issue == "DiskProbeFailed") | .samples')" == "1" ]] ||
  fail "runtime summary did not include fixed issue history"

alert_observed_at="$(date --utc +'%Y-%m-%dT%H:%M:%SZ')"
alert_payload="$(
  jq -cn \
    --arg observedAt "$alert_observed_at" \
    '{
      fingerprint:"platform:smoke",
      code:"Infrastructure.Smoke",
      source:"Infrastructure",
      severity:"Critical",
      active:true,
      title:"隔离告警链路测试",
      summary:"用于验证外部巡检、统一收件箱和确认审计。",
      observedAt:$observedAt
    }'
)"
alert_status="$(
  curl --silent --show-error \
    --output "${response_root}/alert-active.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "X-Hechao-Monitor-Token: ${alert_token}" \
    --data "$alert_payload" \
    "${base_url}/v1/internal/operational-alerts/events"
)"
assert_status 202 "$alert_status" "operational alert activation"

wrong_alert_status="$(
  curl --silent --show-error \
    --output "${response_root}/alert-wrong-token.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header 'X-Hechao-Monitor-Token: wrong-token' \
    --data "$alert_payload" \
    "${base_url}/v1/internal/operational-alerts/events"
)"
assert_status 401 "$wrong_alert_status" "operational alert token rejection"

active_snapshot_status="$(
  curl --silent --show-error \
    --output "${response_root}/alert-snapshot.json" \
    --write-out '%{http_code}' \
    --header "X-Hechao-Monitor-Token: ${alert_token}" \
    "${base_url}/v1/internal/operational-alerts/active"
)"
assert_status 200 "$active_snapshot_status" "active operational alert snapshot"
[[ "$(json_value "${response_root}/alert-snapshot.json" '.alerts[] | select(.fingerprint == "platform:smoke") | .severity')" == "Critical" ]] ||
  fail "internal alert snapshot did not include the active event"

alert_summary_status="$(
  admin_get \
    "/v1/admin/operational-alerts" \
    "${response_root}/alert-summary-active.json"
)"
assert_status 200 "$alert_summary_status" "admin operational alert summary"
[[ "$(json_value "${response_root}/alert-summary-active.json" '.alerts[] | select(.fingerprint == "platform:smoke") | .status')" == "Active" ]] ||
  fail "admin alert summary did not include the active event"

ack_status="$(
  admin_json_write POST \
    "/v1/admin/operational-alerts/platform%3Asmoke/acknowledge" \
    '{}' \
    "${response_root}/alert-acknowledged.json"
)"
assert_status 204 "$ack_status" "operational alert acknowledgement"
alert_summary_status="$(
  admin_get \
    "/v1/admin/operational-alerts" \
    "${response_root}/alert-summary-acknowledged.json"
)"
assert_status 200 "$alert_summary_status" "acknowledged alert summary"
[[ "$(json_value "${response_root}/alert-summary-acknowledged.json" '.alerts[] | select(.fingerprint == "platform:smoke") | .acknowledgedAt != null')" == "true" ]] ||
  fail "operational alert acknowledgement was not persisted"

resolved_payload="$(
  jq -c '.active = false' <<<"$alert_payload"
)"
resolved_status="$(
  curl --silent --show-error \
    --output "${response_root}/alert-resolved.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "X-Hechao-Monitor-Token: ${alert_token}" \
    --data "$resolved_payload" \
    "${base_url}/v1/internal/operational-alerts/events"
)"
assert_status 202 "$resolved_status" "operational alert recovery"
alert_summary_status="$(
  admin_get \
    "/v1/admin/operational-alerts" \
    "${response_root}/alert-summary-resolved.json"
)"
assert_status 200 "$alert_summary_status" "resolved alert summary"
[[ "$(json_value "${response_root}/alert-summary-resolved.json" '.alerts[] | select(.fingerprint == "platform:smoke") | .status')" == "Resolved" ]] ||
  fail "operational alert recovery was not persisted"

create_payload="$(
  jq -cn \
    --arg id "smoke-profile-${suffix}" \
    --arg displayName "Smoke Profile ${suffix}" \
    '{id:$id,displayName:$displayName}'
)"
create_status="$(
  admin_json_write POST \
    "/v1/admin/catalog/client-profiles" \
    "$create_payload" \
    "${response_root}/profile-create.json"
)"
assert_status 201 "$create_status" "profile creation"
[[ "$(json_value "${response_root}/profile-create.json" '.profile.channels | length')" == "3" ]] ||
  fail "new profile did not receive three release channels"
[[ "$(json_value "${response_root}/profile-create.json" '.profile.isActive')" == "false" ]] ||
  fail "new profile must start disabled"

import_manifest() {
  local manifest="$1"
  local output="$2"
  curl --silent --show-error \
    --output "$output" \
    --write-out '%{http_code}' \
    --request POST \
    --header 'Content-Type: application/vnd.hechao.signed-manifest+json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data-binary "@${manifest}" \
    "${base_url}/v1/admin/catalog/client-profiles/${profile_id}/releases"
}

older_import_status="$(
  import_manifest "$older_manifest" "${response_root}/older-import.json"
)"
assert_status 201 "$older_import_status" "older signed release import"
newer_import_status="$(
  import_manifest "$newer_manifest" "${response_root}/newer-import.json"
)"
assert_status 201 "$newer_import_status" "newer signed release import"

for digest in "$older_sha256" "$newer_sha256"; do
  stored="${manifest_root}/releases/${profile_id}/${digest}.json"
  [[ -f "$stored" ]] || fail "immutable manifest is missing: ${digest}"
  [[ "$(sha256sum "$stored" | awk '{print $1}')" == "$digest" ]] ||
    fail "immutable manifest checksum mismatch: ${digest}"
done

detail_file="${response_root}/profile-detail.json"
detail_status="$(
  admin_get \
    "/v1/admin/catalog/client-profiles/${profile_id}" \
    "$detail_file"
)"
assert_status 200 "$detail_status" "profile detail"
[[ "$(json_value "$detail_file" --arg sha "$newer_sha256" '.releases[] | select(.manifestSha256 == $sha) | .minecraftVersion')" != "legacy" ]] ||
  fail "migrated current release metadata was not hydrated"

channel_revision() {
  local channel="$1"
  json_value "$detail_file" \
    --arg channel "$channel" \
    '.profile.channels[] | select(.channel == $channel) | .revision'
}

refresh_detail() {
  local status
  status="$(
    admin_get \
      "/v1/admin/catalog/client-profiles/${profile_id}" \
      "$detail_file"
  )"
  assert_status 200 "$status" "profile detail refresh"
}

set_channel() {
  local channel="$1"
  local digest="$2"
  local percentage="$3"
  local revision="$4"
  local output="$5"
  local payload
  payload="$(
    jq -cn \
      --arg manifestSha256 "$digest" \
      --argjson rolloutPercentage "$percentage" \
      --argjson expectedRevision "$revision" \
      '{manifestSha256:$manifestSha256,rolloutPercentage:$rolloutPercentage,expectedRevision:$expectedRevision}'
  )"
  admin_json_write PUT \
    "/v1/admin/catalog/client-profiles/${profile_id}/channels/${channel}" \
    "$payload" \
    "$output"
}

test_revision="$(channel_revision Test)"
status="$(
  set_channel Test "$older_sha256" 100 "$test_revision" \
    "${response_root}/test-older.json"
)"
assert_status 200 "$status" "test channel older assignment"
refresh_detail
test_revision="$(channel_revision Test)"
status="$(
  set_channel Test "$newer_sha256" 100 "$test_revision" \
    "${response_root}/test-newer.json"
)"
assert_status 200 "$status" "test channel newer assignment"
refresh_detail
test_revision="$(channel_revision Test)"
rollback_payload="$(jq -cn --argjson expectedRevision "$test_revision" '{expectedRevision:$expectedRevision}')"
rollback_status="$(
  admin_json_write POST \
    "/v1/admin/catalog/client-profiles/${profile_id}/channels/Test/rollback" \
    "$rollback_payload" \
    "${response_root}/test-rollback.json"
)"
assert_status 200 "$rollback_status" "test channel rollback"
[[ "$(json_value "${response_root}/test-rollback.json" '.profile.channels[] | select(.channel == "Test") | .manifestSha256')" == "$older_sha256" ]] ||
  fail "test channel did not roll back by publication chronology"

refresh_detail
gray_revision="$(channel_revision Gray)"
status="$(
  set_channel Gray "$newer_sha256" 25 "$gray_revision" \
    "${response_root}/gray-newer.json"
)"
assert_status 200 "$status" "gray channel assignment"

refresh_detail
release_revision="$(
  json_value "$detail_file" \
    --arg sha "$newer_sha256" \
    '.releases[] | select(.manifestSha256 == $sha) | .revision'
)"
pause_payload="$(
  jq -cn \
    --arg reason "isolated smoke rollback" \
    --argjson expectedRevision "$release_revision" \
    '{isPaused:true,reason:$reason,expectedRevision:$expectedRevision}'
)"
pause_status="$(
  admin_json_write PUT \
    "/v1/admin/catalog/client-profiles/${profile_id}/releases/${newer_sha256}/pause" \
    "$pause_payload" \
    "${response_root}/release-paused.json"
)"
assert_status 200 "$pause_status" "release pause"
[[ "$(json_value "${response_root}/release-paused.json" --arg sha "$older_sha256" '.profile.channels[] | select(.channel == "Production") | .manifestSha256 == $sha')" == "true" ]] ||
  fail "production channel did not roll back after pause"
[[ "$(json_value "${response_root}/release-paused.json" --arg sha "$older_sha256" '.profile.channels[] | select(.channel == "Gray") | .manifestSha256 == $sha')" == "true" ]] ||
  fail "gray channel did not roll back after pause"

catalog_status="$(
  curl --silent --show-error \
    --output "${response_root}/catalog.json" \
    --write-out '%{http_code}' \
    --header "Authorization: Bearer ${admin_access_token}" \
    "${base_url}/v1/catalog"
)"
assert_status 200 "$catalog_status" "administrator catalog"
[[ "$(json_value "${response_root}/catalog.json" --arg id "$profile_id" '.clientProfiles[] | select(.id == $id) | .sha256')" == "$older_sha256" ]] ||
  fail "catalog did not resolve the rolled-back test release"

refresh_detail
release_revision="$(
  json_value "$detail_file" \
    --arg sha "$newer_sha256" \
    '.releases[] | select(.manifestSha256 == $sha) | .revision'
)"
resume_payload="$(
  jq -cn \
    --argjson expectedRevision "$release_revision" \
    '{isPaused:false,reason:"",expectedRevision:$expectedRevision}'
)"
resume_status="$(
  admin_json_write PUT \
    "/v1/admin/catalog/client-profiles/${profile_id}/releases/${newer_sha256}/pause" \
    "$resume_payload" \
    "${response_root}/release-resumed.json"
)"
assert_status 200 "$resume_status" "release resume"
[[ "$(json_value "${response_root}/release-resumed.json" --arg sha "$older_sha256" '.profile.channels[] | select(.channel == "Production") | .manifestSha256 == $sha')" == "true" ]] ||
  fail "resume unexpectedly promoted the release"

refresh_detail
gray_revision="$(channel_revision Gray)"
stale_revision=$((gray_revision - 1))
stale_status="$(
  set_channel Gray "$newer_sha256" 25 "$stale_revision" \
    "${response_root}/gray-stale.json"
)"
assert_status 409 "$stale_status" "stale channel revision"

audit_count="$(
  docker exec -u postgres "$container" \
    psql --username="$database_admin" --dbname="$database_name" \
    --tuples-only --no-align \
    --command="
      SELECT count(*)
      FROM launcher.audit_logs
      WHERE actor_user_id = '${admin_user_id}'
        AND action IN (
          'catalog.client_profile.created',
          'catalog.client_profile_release.imported',
          'catalog.client_profile_release.hydrated',
          'catalog.client_profile_channel.updated',
          'catalog.client_profile_channel.rolled_back',
          'catalog.client_profile_release.paused',
          'catalog.client_profile_release.resumed'
        );"
)"
((audit_count >= 8)) || fail "release audit trail is incomplete"

echo "PASS: API 0.20.0 isolated profile, telemetry, runtime and alert smoke test"
echo "Evidence: migrations=15-17, telemetry-idempotency=verified, telemetry-summary=verified"
echo "Evidence: runtime-idempotency=verified, process-tick-disk-summary=verified"
echo "Evidence: alert-auth-lifecycle-acknowledgement=verified"
echo "Evidence: signed-import=verified, immutable-storage=verified"
echo "Evidence: test-gray-production=verified, rollback=verified, pause-auto-rollback=verified"
echo "Evidence: resume-no-promote=verified, catalog-cohort=verified, revision-conflict=verified"

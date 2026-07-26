#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "smoke-test-admin-account-security.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -lt 3 || "$#" -gt 4 ]]; then
  echo "usage: smoke-test-admin-account-security.sh <archive> <sha256> <database-backup> [port]" >&2
  exit 1
fi

archive="$1"
expected_sha256="${2,,}"
database_backup="$3"
port="${4:-18090}"
container="hechao-launcher-postgres"
database_admin="hechao_db_admin"
database_owner="hechao_api"
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
database_name="hechao_security_smoke_$(printf '%s' "$run_id" | tr '[:upper:]-' '[:lower:]_')"
work_root="/tmp/hechao-account-security-smoke-${run_id}"
candidate_root="${work_root}/candidate"
environment_file="${work_root}/environment"
response_root="${work_root}/responses"
log_file="${work_root}/candidate.log"
unit_name="hechao-api-security-smoke-${run_id}"
base_url="http://127.0.0.1:${port}"
forwarded_proto_header="X-Forwarded-Proto: https"
keys_path="/tmp/hechao-account-security-smoke-keys-${run_id}"
diagnostics_path="/tmp/hechao-account-security-smoke-diagnostics-${run_id}"
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
  local filter="$2"
  jq -er "$filter" "$path"
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
    tail -n 80 "$log_file" >&2 || true
  fi

  rm -rf -- "$work_root" "$keys_path" "$diagnostics_path"
  exit "$exit_code"
}
trap cleanup EXIT

for tool in curl docker jq openssl python3 sha256sum systemctl tar; do
  command -v "$tool" >/dev/null || fail "required tool is missing: $tool"
done

[[ -f "$archive" ]] || fail "candidate archive does not exist"
[[ -f "$database_backup" ]] || fail "database backup does not exist"
[[ "$port" =~ ^[0-9]+$ ]] || fail "port must be numeric"
((port >= 1024 && port <= 65535)) || fail "port is outside the allowed range"

actual_sha256="$(sha256sum "$archive" | awk '{print $1}')"
[[ "$actual_sha256" == "$expected_sha256" ]] ||
  fail "candidate archive checksum mismatch"
pg_restore --list "$database_backup" >/dev/null

if ss -H -ltn "sport = :${port}" | grep -q .; then
  fail "candidate port ${port} is already in use"
fi

install -d -o root -g root -m 0755 "$work_root" "$candidate_root" "$response_root"
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
velocity_token="$(openssl rand -hex 32)"
velocity_token_hash="$(printf '%s' "$velocity_token" | sha256sum | awk '{print $1}')"

python3 - \
  /etc/hechao-launcher-api/environment \
  "$environment_file" \
  "$database_name" \
  "$port" \
  "$forum_token_hash" \
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
    forum_hash,
    velocity_hash,
    keys_path,
    diagnostics_path,
) = sys.argv[1:]
lines = open(source, "r", encoding="utf-8").read().splitlines()
updated = []
connection_found = False
overrides = {
    "AdminWeb__Enabled",
    "AdminWeb__PublicBaseUrl",
    "AdminWeb__DataProtectionKeyPath",
    "DiagnosticUploads__StorageRoot",
    "ForumAccountBridge__InternalTokenSha256",
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
        "AdminWeb__Enabled=true",
        f"AdminWeb__PublicBaseUrl=http://127.0.0.1:{port}",
        f"AdminWeb__DataProtectionKeyPath={keys_path}",
        f"DiagnosticUploads__StorageRoot={diagnostics_path}",
        f"ForumAccountBridge__InternalTokenSha256={forum_hash}",
        f"VelocityAuthorization__InternalTokenSha256={velocity_hash}",
    ]
)
open(destination, "w", encoding="utf-8", newline="\n").write("\n".join(updated) + "\n")
PY

chmod 0600 "$environment_file"
install -d -o hechao-api -g hechao-api -m 0700 "$keys_path" "$diagnostics_path"
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
[[ "$(json_value "${response_root}/ready.json" '.version')" == "0.15.0" ]] ||
  fail "candidate reported an unexpected version"

migration_count="$(
  docker exec -u postgres "$container" \
    psql \
    --username="$database_admin" \
    --dbname="$database_name" \
    --tuples-only \
    --no-align \
    --command="SELECT count(*) FROM launcher.schema_migrations WHERE version = 11;"
)"
[[ "$migration_count" == "1" ]] || fail "migration 11 was not applied"

suffix="$(openssl rand -hex 4)"
admin_username="smkadm${suffix}"
target_username="smkusr${suffix}"
admin_display="Smoke Admin ${suffix}"
target_display="Smoke User ${suffix}"
admin_email="${admin_username}@example.invalid"
target_email="${target_username}@example.invalid"
admin_password="SmokeA9$(openssl rand -hex 10)"
target_password="SmokeU9$(openssl rand -hex 10)"
admin_uuid="$(python3 -c 'import uuid; print(uuid.uuid4())')"
target_uuid="$(python3 -c 'import uuid; print(uuid.uuid4())')"
admin_minecraft_name="Adm${suffix}"
target_minecraft_name="Usr${suffix}"

register_account() {
  local username="$1"
  local display_name="$2"
  local email="$3"
  local password="$4"
  local output="$5"
  local payload
  local status
  payload="$(
    jq -cn \
      --arg username "$username" \
      --arg displayName "$display_name" \
      --arg email "$email" \
      --arg password "$password" \
      '{username:$username,displayName:$displayName,email:$email,password:$password}'
  )"
  status="$(
    curl --silent --show-error \
      --output "$output" \
      --write-out '%{http_code}' \
      --header 'Content-Type: application/json' \
      --header "X-Hechao-Forum-Token: ${forum_token}" \
      --data "$payload" \
      "${base_url}/v1/internal/forum/accounts/register"
  )"
  assert_status 201 "$status" "forum account registration"
}

register_account \
  "$admin_username" "$admin_display" "$admin_email" "$admin_password" \
  "${response_root}/admin-register.json"
register_account \
  "$target_username" "$target_display" "$target_email" "$target_password" \
  "${response_root}/target-register.json"
admin_user_id="$(json_value "${response_root}/admin-register.json" '.userId')"
target_user_id="$(json_value "${response_root}/target-register.json" '.userId')"

sql="$(
  cat <<SQL
UPDATE launcher.users
SET access_tier = 'Administrator', updated_at = now()
WHERE id = '${admin_user_id}';
INSERT INTO launcher.minecraft_identities
    (minecraft_uuid, user_id, minecraft_name, verified_at, updated_at,
     luckperms_primary_group, luckperms_synced_at)
VALUES
    ('${admin_uuid}', '${admin_user_id}', '${admin_minecraft_name}',
     now(), now(), 'owner', now()),
    ('${target_uuid}', '${target_user_id}', '${target_minecraft_name}',
     now(), now(), 'default', now());
SQL
)"
docker exec -u postgres "$container" \
  psql \
  --username="$database_admin" \
  --dbname="$database_name" \
  --set=ON_ERROR_STOP=1 \
  --command="$sql" \
  >/dev/null

login_account() {
  local username="$1"
  local password="$2"
  local user_agent="$3"
  local output="$4"
  local expected_status="${5:-200}"
  local payload
  local status
  payload="$(
    jq -cn \
      --arg usernameOrEmail "$username" \
      --arg password "$password" \
      '{usernameOrEmail:$usernameOrEmail,password:$password}'
  )"
  status="$(
    curl --silent --show-error \
      --output "$output" \
      --write-out '%{http_code}' \
      --header 'Content-Type: application/json' \
      --header "User-Agent: ${user_agent}" \
      --data "$payload" \
      "${base_url}/v1/auth/login"
  )"
  assert_status "$expected_status" "$status" "launcher account login"
}

login_account \
  "$admin_username" "$admin_password" "security-smoke-admin" \
  "${response_root}/admin-login.json"
admin_access_token="$(json_value "${response_root}/admin-login.json" '.accessToken')"

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
assert_status 200 "$ticket_status" "admin browser ticket creation"
browser_url="$(json_value "${response_root}/ticket.json" '.browserUrl')"
ticket="$(
  python3 - "$browser_url" <<'PY'
import sys
import urllib.parse

fragment = urllib.parse.urlparse(sys.argv[1]).fragment
values = urllib.parse.parse_qs(fragment).get("ticket", [])
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
assert_status 200 "$csrf_status" "CSRF token creation"
csrf_cookie="$(
  sed -n \
    's/^set-cookie: __Host-HechaoAdminCsrf=\([^;]*\).*/\1/ip' \
    "${response_root}/csrf.headers" |
    head -n 1 |
    tr -d '\r'
)"
csrf_token="$(json_value "${response_root}/csrf.json" '.requestToken')"
[[ -n "$csrf_cookie" && -n "$csrf_token" ]] || fail "CSRF token pair is missing"
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
  fail "MFA session was not marked verified"
[[ "$(json_value "${response_root}/mfa-confirm.json" '.recoveryCodes | length')" == "8" ]] ||
  fail "MFA recovery code count is incorrect"

login_account \
  "$target_username" "$target_password" "security-smoke-target-a" \
  "${response_root}/target-login-a.json"
login_account \
  "$target_username" "$target_password" "security-smoke-target-b" \
  "${response_root}/target-login-b.json"
target_access_token="$(json_value "${response_root}/target-login-a.json" '.accessToken')"

grant_payload='{"serverId":"lobby"}'
grant_status="$(
  curl --silent --show-error \
    --output "${response_root}/target-grant.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "Authorization: Bearer ${target_access_token}" \
    --data "$grant_payload" \
    "${base_url}/v1/velocity/launch-grants"
)"
assert_status 200 "$grant_status" "target Velocity launch grant"

security_status="$(
  curl --silent --show-error \
    --output "${response_root}/security-initial.json" \
    --write-out '%{http_code}' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    "${base_url}/v1/admin/users/${target_user_id}/security"
)"
assert_status 200 "$security_status" "initial account security query"
initial_sessions="$(json_value "${response_root}/security-initial.json" '.launcherSessions | length')"
((initial_sessions >= 2)) || fail "target device sessions were not created"
session_id="$(json_value "${response_root}/security-initial.json" '.launcherSessions[0].sessionId')"

reason_payload='{"reason":"isolated release smoke test"}'
revoke_one_status="$(
  curl --silent --show-error \
    --output "${response_root}/revoke-one.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$reason_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/sessions/${session_id}/revoke"
)"
assert_status 200 "$revoke_one_status" "single device session revocation"

revoke_all_status="$(
  curl --silent --show-error \
    --output "${response_root}/revoke-all.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$reason_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/sessions/revoke-all"
)"
assert_status 200 "$revoke_all_status" "all-session revocation"
[[ "$(json_value "${response_root}/revoke-all.json" '.security.launcherSessions | length')" == "0" ]] ||
  fail "all-session revocation left an active launcher session"
[[ "$(json_value "${response_root}/revoke-all.json" '.revoked.velocityLaunchGrants')" == "1" ]] ||
  fail "all-session revocation did not revoke the pending Velocity grant"

revoked_me_status="$(
  curl --silent --show-error \
    --output "${response_root}/revoked-me.json" \
    --write-out '%{http_code}' \
    --header "Authorization: Bearer ${target_access_token}" \
    "${base_url}/v1/me"
)"
assert_status 401 "$revoked_me_status" "revoked access token rejection"

login_account \
  "$target_username" "$target_password" "security-smoke-target-c" \
  "${response_root}/target-login-c.json"
target_access_token="$(json_value "${response_root}/target-login-c.json" '.accessToken')"

ban_payload='{"reason":"isolated UUID ban smoke test","expiresAt":null,"expectedRevision":null}'
ban_status="$(
  curl --silent --show-error \
    --output "${response_root}/ban.json" \
    --write-out '%{http_code}' \
    --request PUT \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$ban_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/minecraft-ban"
)"
assert_status 200 "$ban_status" "Minecraft UUID ban"
ban_revision="$(json_value "${response_root}/ban.json" '.security.minecraftIdentityBan.revision')"
[[ "$(json_value "${response_root}/ban.json" '.security.launcherSessions | length')" == "0" ]] ||
  fail "Minecraft UUID ban left an active launcher session"

velocity_payload="$(
  jq -cn \
    --arg minecraftUuid "$target_uuid" \
    --arg minecraftName "$target_minecraft_name" \
    '{
      minecraftUuid:$minecraftUuid,
      minecraftName:$minecraftName,
      velocityTarget:"lobby",
      initialConnection:true,
      remoteAddress:null,
      proxyInstance:"security-smoke"
    }'
)"
velocity_status="$(
  curl --silent --show-error \
    --output "${response_root}/velocity-ban.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "X-Hechao-Velocity-Token: ${velocity_token}" \
    --data "$velocity_payload" \
    "${base_url}/v1/internal/velocity/authorize"
)"
assert_status 200 "$velocity_status" "banned UUID Velocity authorization"
[[ "$(json_value "${response_root}/velocity-ban.json" '.reason')" == "MinecraftIdentityBanned" ]] ||
  fail "Velocity did not report MinecraftIdentityBanned"

banned_profile_status="$(
  curl --silent --show-error \
    --output "${response_root}/banned-profile.json" \
    --write-out '%{http_code}' \
    --header "Authorization: Bearer ${target_access_token}" \
    "${base_url}/v1/profiles/base-1.21.11/manifest"
)"
assert_status 401 "$banned_profile_status" "banned account profile access"

update_payload="$(
  jq -cn \
    --arg reason "updated isolated UUID ban smoke test" \
    --argjson expectedRevision "$ban_revision" \
    '{reason:$reason,expiresAt:null,expectedRevision:$expectedRevision}'
)"
update_status="$(
  curl --silent --show-error \
    --output "${response_root}/ban-update.json" \
    --write-out '%{http_code}' \
    --request PUT \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$update_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/minecraft-ban"
)"
assert_status 200 "$update_status" "Minecraft UUID ban update"
current_ban_revision="$(
  json_value "${response_root}/ban-update.json" \
    '.security.minecraftIdentityBan.revision'
)"
((current_ban_revision > ban_revision)) ||
  fail "Minecraft UUID ban revision did not advance"

conflict_revision="$ban_revision"
conflict_payload="$(
  jq -cn \
    --arg reason "stale revision smoke test" \
    --argjson expectedRevision "$conflict_revision" \
    '{reason:$reason,expiresAt:null,expectedRevision:$expectedRevision}'
)"
conflict_status="$(
  curl --silent --show-error \
    --output "${response_root}/ban-conflict.json" \
    --write-out '%{http_code}' \
    --request PUT \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$conflict_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/minecraft-ban"
)"
assert_status 409 "$conflict_status" "Minecraft UUID ban revision conflict"

unban_payload="$(
  jq -cn \
    --arg reason "isolated UUID unban smoke test" \
    --argjson expectedRevision "$current_ban_revision" \
    '{reason:$reason,expectedRevision:$expectedRevision}'
)"
unban_status="$(
  curl --silent --show-error \
    --output "${response_root}/unban.json" \
    --write-out '%{http_code}' \
    --request DELETE \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$unban_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/minecraft-ban"
)"
assert_status 200 "$unban_status" "Minecraft UUID unban"
login_account \
  "$target_username" "$target_password" "security-smoke-target-after-unban" \
  "${response_root}/target-login-after-unban.json"

disable_status="$(
  curl --silent --show-error \
    --output "${response_root}/disable.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$reason_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/account/disable"
)"
assert_status 200 "$disable_status" "account disable"
login_account \
  "$target_username" "$target_password" "security-smoke-disabled" \
  "${response_root}/disabled-login.json" 401

enable_status="$(
  curl --silent --show-error \
    --output "${response_root}/enable.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$reason_payload" \
    "${base_url}/v1/admin/users/${target_user_id}/account/enable"
)"
assert_status 200 "$enable_status" "account enable"
login_account \
  "$target_username" "$target_password" "security-smoke-enabled" \
  "${response_root}/enabled-login.json"

self_disable_status="$(
  curl --silent --show-error \
    --output "${response_root}/self-disable.json" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --header "$forwarded_proto_header" \
    --header "Cookie: ${admin_cookie_header}" \
    --header "X-CSRF-TOKEN: ${csrf_token}" \
    --data "$reason_payload" \
    "${base_url}/v1/admin/users/${admin_user_id}/account/disable"
)"
assert_status 409 "$self_disable_status" "administrator self-protection"

audit_count="$(
  docker exec -u postgres "$container" \
    psql \
    --username="$database_admin" \
    --dbname="$database_name" \
    --tuples-only \
    --no-align \
    --command="
      SELECT count(DISTINCT action)
      FROM launcher.audit_logs
      WHERE actor_user_id = '${admin_user_id}'
        AND action IN (
          'security.account.disabled',
          'security.account.enabled',
          'security.sessions.revoked_all',
          'security.session.revoked',
          'security.minecraft_ban.created',
          'security.minecraft_ban.revoked'
        );"
)"
[[ "$audit_count" == "6" ]] || fail "security audit actions are incomplete"

echo "PASS: API 0.15.0 isolated account-security smoke test"
echo "Evidence: migration=11, MFA=enrolled, session-revocation=verified, UUID-ban=verified"
echo "Evidence: Velocity-ban=verified, revision-conflict=verified, disable-enable=verified"

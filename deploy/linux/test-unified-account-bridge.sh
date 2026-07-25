#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "test-unified-account-bridge.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 2 ]]; then
  echo "usage: test-unified-account-bridge.sh <api-archive> <sha256>" >&2
  exit 1
fi

archive="$1"
expected_sha256="${2,,}"
environment_source="/etc/hechao-launcher-api/environment"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
database_name="hechao_unified_test_${timestamp,,}"
database_name="${database_name//[^a-z0-9_]/_}"
unit_name="hechao-unified-test-${timestamp,,}"
unit_name="${unit_name//[^a-z0-9-]/-}"
work_directory="/opt/hechao-launcher-api/integration-tests/${timestamp}"
environment_file="/etc/hechao-launcher-api/unified-test-${timestamp}.environment"
response_file="${work_directory}/response.json"
test_port="18090"

report_failure() {
  echo "unified account bridge test failed at line ${BASH_LINENO[0]}" >&2
}

cleanup() {
  systemctl stop "${unit_name}.service" >/dev/null 2>&1 || true
  docker exec -u postgres hechao-launcher-postgres \
    dropdb --username=hechao_db_admin --if-exists --force "$database_name" \
    >/dev/null 2>&1 || true

  case "$work_directory" in
    /opt/hechao-launcher-api/integration-tests/*)
      rm -rf "$work_directory"
      ;;
  esac
  case "$environment_file" in
    /etc/hechao-launcher-api/unified-test-*.environment)
      rm -f "$environment_file"
      ;;
  esac
}
trap report_failure ERR
trap cleanup EXIT

command -v curl >/dev/null
command -v docker >/dev/null
command -v jq >/dev/null
command -v openssl >/dev/null
command -v systemd-run >/dev/null
test -f "$archive"
test -f "$environment_source"
test "$(sha256sum "$archive" | awk '{print $1}')" = "$expected_sha256"
if ss -lnt "( sport = :${test_port} )" | grep -q LISTEN; then
  echo "test port ${test_port} is already in use" >&2
  exit 1
fi

install -d -o root -g root -m 0755 "$work_directory"
tar -xzf "$archive" -C "$work_directory"
chown -R root:root "$work_directory"
find "$work_directory" -type d -exec chmod 0755 {} +
find "$work_directory" -type f -exec chmod 0444 {} +
chmod 0555 "$work_directory/Hechao.Api"

docker exec -u postgres hechao-launcher-postgres \
  createdb --username=hechao_db_admin --owner=hechao_api "$database_name"

install -o root -g root -m 0600 "$environment_source" "$environment_file"
sed -i "s/Database=hechao_launcher/Database=${database_name}/g" "$environment_file"
if ! grep -q "Database=${database_name}" "$environment_file"; then
  echo "temporary connection string was not updated" >&2
  exit 1
fi

bridge_token="$(openssl rand -hex 32)"
bridge_hash="$(printf '%s' "$bridge_token" | sha256sum | awk '{print $1}')"
{
  echo "urls=http://127.0.0.1:${test_port}"
  echo "ForumAccountBridge__InternalTokenSha256=${bridge_hash}"
  echo "ForumAccountBridge__AllowLegacyImport=true"
} >> "$environment_file"

systemd-run --quiet --unit="$unit_name" --collect \
  --property=Type=simple \
  --property=User=hechao-api \
  --property=Group=hechao-api \
  --property="WorkingDirectory=${work_directory}" \
  --property="EnvironmentFile=${environment_file}" \
  "$work_directory/Hechao.Api"

ready=false
for _ in $(seq 1 30); do
  if curl -fsS --max-time 2 "http://127.0.0.1:${test_port}/readyz" \
      >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 1
done
if [[ "$ready" != true ]]; then
  journalctl -u "${unit_name}.service" -n 60 --no-pager >&2
  exit 1
fi

migration="$(
  docker exec -u postgres hechao-launcher-postgres \
    psql --username=hechao_db_admin --dbname="$database_name" \
    --tuples-only --no-align \
    --command='SELECT max(version) FROM launcher.schema_migrations;'
)"
test "$migration" = "8"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST "http://127.0.0.1:${test_port}/v1/auth/register" \
    -H 'Content-Type: application/json' \
    --data '{
      "username":"bypass_test",
      "displayName":"绕过测试",
      "email":"bypass@example.invalid",
      "password":"BypassPass123"
    }'
)"
test "$status" = "426"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    "http://127.0.0.1:${test_port}/v1/internal/forum/accounts/authenticate" \
    -H 'Content-Type: application/json' \
    -H 'X-Hechao-Forum-Token: invalid' \
    --data '{"usernameOrEmail":"missing","password":"WrongPass123"}'
)"
test "$status" = "401"

legacy_import='{
  "forumUserId":"900000001",
  "username":"unified_test",
  "displayName":"统一账号测试",
  "email":"unified-test@example.invalid",
  "passwordHash":"scrypt$00112233445566778899aabbccddeeff$159474590650a5c233eb90717fea58b709f096f4a9fceca2fcf8309abe597265af1e99102b38a50a9b0adefbdc32d57f07abb3c190ba771f922db13b10765b9c",
  "isDisabled":false,
  "createdAt":"2026-07-25T00:00:00Z"
}'
status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    "http://127.0.0.1:${test_port}/v1/internal/forum/accounts/import" \
    -H 'Content-Type: application/json' \
    -H "X-Hechao-Forum-Token: ${bridge_token}" \
    --data "$legacy_import"
)"
test "$status" = "200"
user_id="$(jq -er '.account.userId' "$response_file")"
test -n "$user_id"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    "http://127.0.0.1:${test_port}/v1/internal/forum/accounts/import" \
    -H 'Content-Type: application/json' \
    -H "X-Hechao-Forum-Token: ${bridge_token}" \
    --data "$legacy_import"
)"
test "$status" = "200"
jq -e '.created == false' "$response_file" >/dev/null

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST "http://127.0.0.1:${test_port}/v1/auth/login" \
    -H 'Content-Type: application/json' \
    --data '{
      "usernameOrEmail":"unified_test",
      "password":"UnifiedPass123"
    }'
)"
test "$status" = "200"
jq -e '.account.username == "unified_test"' "$response_file" >/dev/null
rehash="$(
  docker exec -u postgres hechao-launcher-postgres \
    psql --username=hechao_db_admin --dbname="$database_name" \
    --tuples-only --no-align \
    --command="SELECT password_hash NOT LIKE 'scrypt$%' FROM launcher.users WHERE id = '${user_id}';"
)"
test "$rehash" = "t"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    "http://127.0.0.1:${test_port}/v1/internal/forum/accounts/password/change" \
    -H 'Content-Type: application/json' \
    -H "X-Hechao-Forum-Token: ${bridge_token}" \
    --data "{
      \"userId\":\"${user_id}\",
      \"currentPassword\":\"UnifiedPass123\",
      \"newPassword\":\"ChangedPass456\"
    }"
)"
test "$status" = "204"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST "http://127.0.0.1:${test_port}/v1/auth/login" \
    -H 'Content-Type: application/json' \
    --data '{
      "usernameOrEmail":"unified_test",
      "password":"UnifiedPass123"
    }'
)"
test "$status" = "401"
status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST "http://127.0.0.1:${test_port}/v1/auth/login" \
    -H 'Content-Type: application/json' \
    --data '{
      "usernameOrEmail":"unified-test@example.invalid",
      "password":"ChangedPass456"
    }'
)"
test "$status" = "200"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    "http://127.0.0.1:${test_port}/v1/internal/forum/accounts/profile" \
    -H 'Content-Type: application/json' \
    -H "X-Hechao-Forum-Token: ${bridge_token}" \
    --data "{
      \"userId\":\"${user_id}\",
      \"displayName\":\"统一账号新昵称\"
    }"
)"
test "$status" = "200"
jq -e '.displayName == "统一账号新昵称"' "$response_file" >/dev/null

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    "http://127.0.0.1:${test_port}/v1/internal/forum/accounts/password/reset" \
    -H 'Content-Type: application/json' \
    -H "X-Hechao-Forum-Token: ${bridge_token}" \
    --data "{
      \"userId\":\"${user_id}\",
      \"newPassword\":\"ResetPass789\"
    }"
)"
test "$status" = "204"
status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST "http://127.0.0.1:${test_port}/v1/auth/login" \
    -H 'Content-Type: application/json' \
    --data '{
      "usernameOrEmail":"unified_test",
      "password":"ChangedPass456"
    }'
)"
test "$status" = "401"
status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST "http://127.0.0.1:${test_port}/v1/auth/login" \
    -H 'Content-Type: application/json' \
    --data '{
      "usernameOrEmail":"unified_test",
      "password":"ResetPass789"
    }'
)"
test "$status" = "200"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    "http://127.0.0.1:${test_port}/v1/internal/forum/accounts/authenticate" \
    -H 'Content-Type: application/json' \
    -H "X-Hechao-Forum-Token: ${bridge_token}" \
    -H 'X-Forwarded-For: 203.0.113.10' \
    --data '{
      "usernameOrEmail":"unified_test",
      "password":"ResetPass789"
    }'
)"
test "$status" = "404"

echo \
  "migration=8 legacy-login=pass rehash=pass password-sync=pass profile-sync=pass loopback-guard=pass"

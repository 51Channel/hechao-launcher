#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "test-production-forum-session-revocation.sh must run as root" >&2
  exit 1
fi

forum_db="/home/ecs-user/hechao/prisma/dev.db"
forum_environment="/home/ecs-user/hechao/.env"
container="hechao-launcher-postgres"
database_admin="hechao_db_admin"
database_name="hechao_launcher"
account_id="$(cat /proc/sys/kernel/random/uuid)"
direct_request="$(cat /proc/sys/kernel/random/uuid)"
worker_request="$(cat /proc/sys/kernel/random/uuid)"
suffix="$(printf '%s' "$account_id" | tr -d '-' | cut -c1-12)"
username="revokesmoke_${suffix}"
email="revokesmoke_${suffix}@example.invalid"
display_name="Revocation Smoke ${suffix}"

fail() {
  echo "FAIL: $*" >&2
  return 1
}

cleanup() {
  local exit_code="$?"
  trap - EXIT
  set +e
  sqlite3 "$forum_db" "
    DELETE FROM \"ForumSessionRevocationReceipt\"
    WHERE \"requestId\" IN ('$direct_request', '$worker_request');
    DELETE FROM \"User\" WHERE \"launcherAccountId\" = '$account_id';
  " >/dev/null
  docker exec -u postgres "$container" \
    psql \
    --username="$database_admin" \
    --dbname="$database_name" \
    --quiet \
    --command="DELETE FROM launcher.users WHERE id = '$account_id';" \
    >/dev/null
  exit "$exit_code"
}
trap cleanup EXIT

for tool in curl docker jq sqlite3; do
  command -v "$tool" >/dev/null || fail "required tool is missing: $tool"
done
test -f "$forum_db"
test -f "$forum_environment"
test "$(systemctl is-active hechao.service)" = "active"
test "$(systemctl is-active hechao-launcher-api.service)" = "active"

sqlite3 "$forum_db" "
  INSERT INTO \"User\"
      (
        \"email\",
        \"passwordHash\",
        \"launcherAccountId\",
        \"launcherUsername\",
        \"sessionVersion\",
        \"displayName\",
        \"emailVerified\"
      )
  VALUES
      (
        '$email',
        'production-smoke-not-a-login',
        '$account_id',
        '$username',
        0,
        '$display_name',
        1
      );
"
docker exec -u postgres "$container" \
  psql \
  --username="$database_admin" \
  --dbname="$database_name" \
  --quiet \
  --command="
    INSERT INTO launcher.users
        (id, display_name, access_tier, username, email)
    VALUES
        ('$account_id', '$display_name', 'Member', '$username', '$email');
  " \
  >/dev/null

token="$(
  sed -n 's/^HECHAO_SESSION_REVOCATION_TOKEN=//p' "$forum_environment" |
    tail -n 1
)"
[[ "${#token}" -ge 32 && "${#token}" -le 256 ]] ||
  fail "forum session-revocation token is invalid"
payload="$(
  jq -cn \
    --arg requestId "$direct_request" \
    --arg userId "$account_id" \
    '{requestId:$requestId,userId:$userId}'
)"
for attempt in 1 2; do
  status="$(
    curl \
      --silent \
      --show-error \
      --output /dev/null \
      --write-out '%{http_code}' \
      --request POST \
      --header 'Content-Type: application/json' \
      --header "X-Hechao-Session-Token: ${token}" \
      --data "$payload" \
      http://127.0.0.1:3000/api/internal/hechao/session-revoke
  )"
  [[ "$status" == "204" ]] ||
    fail "direct request ${attempt} returned HTTP ${status}"
done
unset token

version="$(
  sqlite3 "$forum_db" "
    SELECT \"sessionVersion\"
    FROM \"User\"
    WHERE \"launcherAccountId\" = '$account_id';
  "
)"
receipt_count="$(
  sqlite3 "$forum_db" "
    SELECT count(1)
    FROM \"ForumSessionRevocationReceipt\"
    WHERE \"requestId\" = '$direct_request';
  "
)"
[[ "$version" == "1" ]] ||
  fail "idempotent direct requests changed sessionVersion ${version} times"
[[ "$receipt_count" == "1" ]] ||
  fail "idempotent direct request created ${receipt_count} receipts"

docker exec -u postgres "$container" \
  psql \
  --username="$database_admin" \
  --dbname="$database_name" \
  --quiet \
  --command="
    INSERT INTO launcher.forum_session_revocation_outbox
        (id, user_id, requested_at, next_attempt_at)
    VALUES
        ('$worker_request', '$account_id', now(), now());
  " \
  >/dev/null

completed=false
for _ in {1..30}; do
  row="$(
    docker exec -u postgres "$container" \
      psql \
      --username="$database_admin" \
      --dbname="$database_name" \
      --tuples-only \
      --no-align \
      --command="
        SELECT
          (completed_at IS NOT NULL)::text || '|' ||
          attempt_count::text || '|' ||
          coalesce(last_error, '')
        FROM launcher.forum_session_revocation_outbox
        WHERE id = '$worker_request';
      " |
      tr -d '\r'
  )"
  if [[ "$row" == true\|* ]]; then
    completed=true
    break
  fi
  sleep 1
done
[[ "$completed" == true ]] ||
  fail "API worker did not complete the forum revocation: ${row:-missing}"

version="$(
  sqlite3 "$forum_db" "
    SELECT \"sessionVersion\"
    FROM \"User\"
    WHERE \"launcherAccountId\" = '$account_id';
  "
)"
receipt_count="$(
  sqlite3 "$forum_db" "
    SELECT count(1)
    FROM \"ForumSessionRevocationReceipt\"
    WHERE \"requestId\" = '$worker_request';
  "
)"
[[ "$version" == "2" ]] ||
  fail "API worker did not increment the forum sessionVersion"
[[ "$receipt_count" == "1" ]] ||
  fail "API worker did not create exactly one delivery receipt"

echo "PASS: production forum session revocation"
echo "Evidence: direct-idempotency=verified, API-worker=delivered, cleanup=armed"

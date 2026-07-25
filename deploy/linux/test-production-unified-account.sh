#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "test-production-unified-account.sh must run as root" >&2
  exit 1
fi

umask 077
site_root="/home/ecs-user/hechao"
test_root="$(mktemp -d /root/hechao-unified-production-test.XXXXXX)"
suffix="$(openssl rand -hex 5)"
username="unifiedqa_${suffix}"
email="${username}@example.invalid"
display_name="Unified QA ${suffix}"
updated_display_name="Unified QA2 ${suffix}"
verification_code="654321"
current_password="Qa1$(openssl rand -hex 12)"
next_password="Qb2$(openssl rand -hex 12)"
base_url="https://hechao.world"

cleanup() {
  original_status=$?
  trap - EXIT
  set +e
  runuser -u ecs-user -- env HOME=/home/ecs-user TEST_EMAIL="$email" \
    bash -c 'cd "$1" && node --env-file=.env' bash "$site_root" \
    <<'NODE' >/dev/null 2>&1
const { PrismaClient } = require("@prisma/client");
const prisma = new PrismaClient();
(async () => {
  await prisma.emailCode.deleteMany({ where: { email: process.env.TEST_EMAIL } });
  await prisma.user.deleteMany({ where: { email: process.env.TEST_EMAIL } });
  const remaining = await prisma.user.count({
    where: { email: process.env.TEST_EMAIL },
  });
  if (remaining !== 0) throw new Error("forum cleanup failed");
})().finally(() => prisma.$disconnect());
NODE
  if [[ "$?" -ne 0 ]]; then
    echo "forum synthetic-account cleanup failed" >&2
    original_status=1
  fi

  docker exec -i -u postgres hechao-launcher-postgres \
    psql --username=hechao_db_admin --dbname=hechao_launcher \
    --set=ON_ERROR_STOP=1 --set=test_username="$username" \
    >/dev/null 2>&1 <<'SQL'
BEGIN;
DELETE FROM launcher.audit_logs
WHERE actor_user_id IN (
    SELECT id FROM launcher.users WHERE username = :'test_username'
  )
   OR target_id IN (
    SELECT id::text FROM launcher.users WHERE username = :'test_username'
  );
DELETE FROM launcher.users WHERE username = :'test_username';
COMMIT;
SQL
  if [[ "$?" -ne 0 ]]; then
    echo "central synthetic-account cleanup failed" >&2
    original_status=1
  fi
  central_remaining="$(
    docker exec -u postgres hechao-launcher-postgres \
      psql --username=hechao_db_admin --dbname=hechao_launcher \
      --tuples-only --no-align --set=test_username="$username" \
      --command="SELECT count(*) FROM launcher.users WHERE username = :'test_username';" \
      2>/dev/null
  )"
  if [[ "$central_remaining" != "0" ]]; then
    echo "central synthetic-account cleanup verification failed" >&2
    original_status=1
  fi

  case "$test_root" in
    /root/hechao-unified-production-test.*)
      rm -rf -- "$test_root" || original_status=1
      ;;
    *)
      echo "refusing to remove unexpected test directory" >&2
      original_status=1
      ;;
  esac
  exit "$original_status"
}
trap cleanup EXIT

test -d "$site_root"
test -f "${site_root}/.env"
test "$(systemctl is-active hechao.service)" = "active"
test "$(systemctl is-active hechao-launcher-api.service)" = "active"

runuser -u ecs-user -- env \
  HOME=/home/ecs-user \
  TEST_EMAIL="$email" \
  TEST_CODE="$verification_code" \
  bash -c 'cd "$1" && node --env-file=.env' bash "$site_root" <<'NODE'
const { randomBytes, scryptSync } = require("node:crypto");
const { PrismaClient } = require("@prisma/client");
const prisma = new PrismaClient();
(async () => {
  const salt = randomBytes(16).toString("hex");
  const digest = scryptSync(process.env.TEST_CODE, salt, 64).toString("hex");
  await prisma.emailCode.upsert({
    where: { email: process.env.TEST_EMAIL },
    create: {
      email: process.env.TEST_EMAIL,
      codeHash: `scrypt$${salt}$${digest}`,
      expiresAt: new Date(Date.now() + 10 * 60 * 1000),
      attempts: 0,
    },
    update: {
      codeHash: `scrypt$${salt}$${digest}`,
      expiresAt: new Date(Date.now() + 10 * 60 * 1000),
      attempts: 0,
    },
  });
})().finally(() => prisma.$disconnect());
NODE

write_json() {
  destination="$1"
  shift
  printf "$@" > "$destination"
}

request() {
  expected_status="$1"
  response_file="$2"
  cookie_arguments="$3"
  body_file="$4"
  endpoint="$5"
  status="$(
    curl -sS \
      --noproxy "*" \
      --resolve hechao.world:443:127.0.0.1 \
      $cookie_arguments \
      -H "Content-Type: application/json" \
      --data-binary "@${body_file}" \
      -o "$response_file" \
      -w "%{http_code}" \
      "${base_url}${endpoint}"
  )"
  if [[ "$status" != "$expected_status" ]]; then
    echo "unexpected HTTP status for ${endpoint}: ${status}" >&2
    return 1
  fi
}

write_json "${test_root}/register.json" \
  '{"username":"%s","email":"%s","displayName":"%s","password":"%s","code":"%s"}' \
  "$username" "$email" "$display_name" "$current_password" "$verification_code"
request 200 "${test_root}/register-response.json" \
  "-c ${test_root}/register.cookies" \
  "${test_root}/register.json" "/api/forum/register"
grep -Fq '"ok":true' "${test_root}/register-response.json"

write_json "${test_root}/login-username.json" \
  '{"identifier":"%s","password":"%s"}' "$username" "$current_password"
request 200 "${test_root}/login-username-response.json" \
  "-c ${test_root}/username.cookies" \
  "${test_root}/login-username.json" "/api/forum/login"

write_json "${test_root}/login-email.json" \
  '{"identifier":"%s","password":"%s"}' "$email" "$current_password"
request 200 "${test_root}/login-email-response.json" \
  "-c ${test_root}/email.cookies" \
  "${test_root}/login-email.json" "/api/forum/login"

write_json "${test_root}/profile.json" \
  '{"displayName":"%s"}' "$updated_display_name"
request 200 "${test_root}/profile-response.json" \
  "-b ${test_root}/register.cookies -c ${test_root}/register.cookies" \
  "${test_root}/profile.json" "/api/forum/settings"
grep -Fq "\"displayName\":\"${updated_display_name}\"" \
  "${test_root}/profile-response.json"

write_json "${test_root}/change-password.json" \
  '{"current":"%s","next":"%s"}' "$current_password" "$next_password"
request 200 "${test_root}/change-password-response.json" \
  "-b ${test_root}/register.cookies -c ${test_root}/register.cookies" \
  "${test_root}/change-password.json" "/api/forum/change-password"

write_json "${test_root}/old-session.json" '{"bio":"session-check"}'
request 401 "${test_root}/old-session-response.json" \
  "-b ${test_root}/username.cookies" \
  "${test_root}/old-session.json" "/api/forum/settings"

request 401 "${test_root}/old-password-response.json" \
  "" "${test_root}/login-username.json" "/api/forum/login"

write_json "${test_root}/new-password.json" \
  '{"identifier":"%s","password":"%s"}' "$email" "$next_password"
request 200 "${test_root}/new-password-response.json" \
  "" "${test_root}/new-password.json" "/api/forum/login"
grep -Fq "\"displayName\":\"${updated_display_name}\"" \
  "${test_root}/new-password-response.json"

echo "registration=pass"
echo "username_login=pass"
echo "email_login=pass"
echo "profile_sync=pass"
echo "session_revocation=pass"
echo "password_change=pass"

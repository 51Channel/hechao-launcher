#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "finalize-forum-unified-identity.sh must run as root" >&2
  exit 1
fi

umask 077
site_root="/home/ecs-user/hechao"
api_environment="/etc/hechao-launcher-api/environment"
retire_log="/var/log/hechao-unified-account/retire-local-passwords.log"
temporary_environment="$(mktemp)"
trap 'rm -f "$temporary_environment"' EXIT

test -f "${site_root}/scripts/import-unified-accounts.mjs"
test -f "${site_root}/.env"
test -f "$api_environment"
test "$(
  grep -c '^ForumAccountBridge__AllowLegacyImport=true$' "$api_environment"
)" -eq 1

runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c \
  'cd "$1" && node --env-file=.env scripts/import-unified-accounts.mjs --apply --retire-local-passwords' \
  bash "$site_root" > "$retire_log" 2>&1
chmod 0600 "$retire_log"

account_counts="$(
  runuser -u ecs-user -- env HOME=/home/ecs-user \
    bash -c 'cd "$1" && node --env-file=.env' bash "$site_root" <<'NODE'
const { PrismaClient } = require("@prisma/client");
const prisma = new PrismaClient();
(async () => {
  const [total, linked, localPasswords] = await Promise.all([
    prisma.user.count(),
    prisma.user.count({ where: { launcherAccountId: { not: null } } }),
    prisma.user.count({ where: { passwordHash: { not: "unified" } } }),
  ]);
  console.log(`${total} ${linked} ${localPasswords}`);
  if (total !== linked || localPasswords !== 0) process.exitCode = 1;
})().finally(() => prisma.$disconnect());
NODE
)"
read -r total_accounts linked_accounts local_passwords <<< "$account_counts"
test "$total_accounts" = "$linked_accounts"
test "$local_passwords" = "0"

sed \
  's/^ForumAccountBridge__AllowLegacyImport=true$/ForumAccountBridge__AllowLegacyImport=false/' \
  "$api_environment" > "$temporary_environment"
test "$(
  grep -c '^ForumAccountBridge__AllowLegacyImport=false$' "$temporary_environment"
)" -eq 1
install -o root -g root -m 0600 \
  "$temporary_environment" "$api_environment"

systemctl restart hechao-launcher-api.service
api_ready=false
for _ in {1..30}; do
  if curl -fsS --max-time 2 http://127.0.0.1:8090/readyz >/dev/null 2>&1; then
    api_ready=true
    break
  fi
  sleep 1
done
test "$api_ready" = true

head -n 1 "$retire_log"
tail -n 2 "$retire_log"
echo "forum_accounts=${total_accounts}"
echo "linked_accounts=${linked_accounts}"
echo "local_password_hashes=${local_passwords}"
echo "legacy_import=disabled"

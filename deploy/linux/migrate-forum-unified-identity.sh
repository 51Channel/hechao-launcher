#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "migrate-forum-unified-identity.sh must run as root" >&2
  exit 1
fi
if [[ "$#" -ne 1 ]]; then
  echo "usage: migrate-forum-unified-identity.sh <staged-forum-directory>" >&2
  exit 1
fi

umask 077
staged_directory="$(readlink -f "$1")"
site_root="/home/ecs-user/hechao"
site_service="hechao.service"
migration_name="20260725065000_unified_hechao_identity"
log_root="/var/log/hechao-unified-account"
dry_run_log="${log_root}/import-dry-run.log"
apply_log="${log_root}/import-apply.log"
site_stopped=false

restart_site_if_needed() {
  if [[ "$site_stopped" == true ]]; then
    systemctl start "$site_service" >/dev/null 2>&1 || true
  fi
}
trap restart_site_if_needed EXIT

case "$staged_directory" in
  /root/hechao-unified-staging/*) ;;
  *)
    echo "unexpected staged directory: $staged_directory" >&2
    exit 1
    ;;
esac

test -f "${staged_directory}/prisma/schema.prisma"
test -f \
  "${staged_directory}/prisma/migrations/${migration_name}/migration.sql"
test -f "${staged_directory}/scripts/import-unified-accounts.mjs"
test -x "${site_root}/node_modules/.bin/prisma"
test -f "${site_root}/.env"
test -f "${site_root}/prisma/dev.db"
test "$(systemctl is-active "$site_service")" = "active"

install -d -o root -g root -m 0700 "$log_root"

systemctl stop "$site_service"
site_stopped=true

install -o ecs-user -g ecs-user -m 0644 \
  "${staged_directory}/prisma/schema.prisma" \
  "${site_root}/prisma/schema.prisma"
install -d -o ecs-user -g ecs-user -m 0755 \
  "${site_root}/prisma/migrations/${migration_name}"
install -o ecs-user -g ecs-user -m 0644 \
  "${staged_directory}/prisma/migrations/${migration_name}/migration.sql" \
  "${site_root}/prisma/migrations/${migration_name}/migration.sql"
install -o ecs-user -g ecs-user -m 0644 \
  "${staged_directory}/scripts/import-unified-accounts.mjs" \
  "${site_root}/scripts/import-unified-accounts.mjs"

runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c 'cd "$1" && ./node_modules/.bin/prisma migrate deploy' \
  bash "$site_root"
runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c 'cd "$1" && ./node_modules/.bin/prisma generate' \
  bash "$site_root"

systemctl start "$site_service"
site_stopped=false

runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c \
  'cd "$1" && node --env-file=.env scripts/import-unified-accounts.mjs' \
  bash "$site_root" > "$dry_run_log" 2>&1
runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c \
  'cd "$1" && node --env-file=.env scripts/import-unified-accounts.mjs --apply' \
  bash "$site_root" > "$apply_log" 2>&1

chmod 0600 "$dry_run_log" "$apply_log"

site_ready=false
for _ in {1..30}; do
  if curl -fsS --max-time 2 http://127.0.0.1:3000/ >/dev/null 2>&1; then
    site_ready=true
    break
  fi
  sleep 1
done
test "$site_ready" = true

grep -E '^(论坛账号：|完成：)' "$dry_run_log"
grep -E '^(论坛账号：|完成：)' "$apply_log"
echo "forum_unified_identity_migration=ready"

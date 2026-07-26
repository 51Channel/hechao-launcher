#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "deploy-forum-session-revocation.sh must run as root" >&2
  exit 1
fi
if [[ "$#" -ne 2 ]]; then
  echo \
    "usage: deploy-forum-session-revocation.sh <staged-overlay-directory> <backup-directory>" \
    >&2
  exit 1
fi

umask 077
staged_directory="$(readlink -f "$1")"
backup_directory="$(readlink -f "$2")"
site_root="/home/ecs-user/hechao"
site_service="hechao.service"
database_relative="prisma/dev.db"
migration_relative="prisma/migrations/20260727090000_forum_session_revocation_receipts/migration.sql"
route_relative="src/app/api/internal/hechao/session-revoke/route.ts"
proxy_relative="src/proxy.ts"
previous_build="${backup_directory}/forum-next-before-session-revocation"
build_log="${backup_directory}/forum-session-revocation-build.log"
deployment_succeeded=false
site_stopped=false

case "$staged_directory" in
  /root/hechao-forum-session-revocation-staging/*) ;;
  *)
    echo "unexpected staged directory: $staged_directory" >&2
    exit 1
    ;;
esac
case "$backup_directory" in
  /var/backups/hechao-forum-session-revocation/*) ;;
  *)
    echo "unexpected backup directory: $backup_directory" >&2
    exit 1
    ;;
esac

test -f "${staged_directory}/${migration_relative}"
test -f "${staged_directory}/${route_relative}"
test -f "${staged_directory}/${proxy_relative}"
test -d "$site_root"
test -d "${site_root}/.next"
test -f "${site_root}/${database_relative}"
test -f "${site_root}/.env"
test "$(systemctl is-active "$site_service")" = "active"
install -d -o root -g root -m 0700 "$backup_directory"

rollback_if_needed() {
  exit_code=$?
  if [[ "$deployment_succeeded" == true ]]; then
    return
  fi

  set +e
  if [[ "$site_stopped" == false ]]; then
    systemctl stop "$site_service"
  fi
  rm -rf \
    "${site_root}/src/app/api/internal/hechao/session-revoke" \
    "${site_root}/prisma/migrations/20260727090000_forum_session_revocation_receipts"
  tar -xzf "${backup_directory}/forum-source.tar.gz" -C "$site_root"
  install -o ecs-user -g ecs-user -m 0600 \
    "${backup_directory}/forum.sqlite" \
    "${site_root}/${database_relative}"
  if [[ -d "${site_root}/.next" ]]; then
    mv "${site_root}/.next" \
      "${backup_directory}/forum-next-failed-session-revocation"
  fi
  if [[ -d "$previous_build" ]]; then
    mv "$previous_build" "${site_root}/.next"
  fi
  systemctl start "$site_service"
  echo "forum deployment failed and the previous state was restored" >&2
  exit "$exit_code"
}
trap rollback_if_needed EXIT

tar -czf "${backup_directory}/forum-source.tar.gz" \
  --exclude='.git' \
  --exclude='.next' \
  --exclude='node_modules' \
  --exclude='.env' \
  --exclude="$database_relative" \
  -C "$site_root" .

systemctl stop "$site_service"
site_stopped=true
install -o root -g root -m 0600 \
  "${site_root}/${database_relative}" \
  "${backup_directory}/forum.sqlite"
mv "${site_root}/.next" "$previous_build"

for relative_path in "$migration_relative" "$route_relative" "$proxy_relative"; do
  destination="${site_root}/${relative_path}"
  install -d -o ecs-user -g ecs-user -m 0755 "$(dirname "$destination")"
  install -o ecs-user -g ecs-user -m 0644 \
    "${staged_directory}/${relative_path}" "$destination"
done

runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c 'cd "$1" && npx prisma migrate deploy && npm run build' \
  bash "$site_root" > "$build_log" 2>&1
chmod 0600 "$build_log"

systemctl start "$site_service"
site_stopped=false

site_ready=false
for _ in {1..60}; do
  if curl -fsS --max-time 2 http://127.0.0.1:3000/ >/dev/null 2>&1; then
    site_ready=true
    break
  fi
  sleep 1
done
test "$site_ready" = true

tar -czf "${backup_directory}/forum-next-before-session-revocation.tar.gz" \
  -C "$backup_directory" "$(basename "$previous_build")"
sha256sum \
  "${backup_directory}/forum.sqlite" \
  "${backup_directory}/forum-source.tar.gz" \
  "${backup_directory}/forum-next-before-session-revocation.tar.gz" \
  > "${backup_directory}/manifest.sha256"
sha256sum -c "${backup_directory}/manifest.sha256" >/dev/null
chmod 0600 "${backup_directory}"/*

deployment_succeeded=true
echo "forum_session_revocation_deployment=ready"
echo "backup_directory=$backup_directory"

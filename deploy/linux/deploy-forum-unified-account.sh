#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "deploy-forum-unified-account.sh must run as root" >&2
  exit 1
fi
if [[ "$#" -ne 2 ]]; then
  echo \
    "usage: deploy-forum-unified-account.sh <staged-forum-directory> <backup-directory>" \
    >&2
  exit 1
fi

umask 077
staged_directory="$(readlink -f "$1")"
backup_directory="$(readlink -f "$2")"
site_root="/home/ecs-user/hechao"
site_service="hechao.service"
previous_build="${backup_directory}/forum-next-before-unified"
build_log="${backup_directory}/forum-unified-build.log"
deployment_succeeded=false

case "$staged_directory" in
  /root/hechao-unified-staging/*) ;;
  *)
    echo "unexpected staged directory: $staged_directory" >&2
    exit 1
    ;;
esac
case "$backup_directory" in
  /var/backups/hechao-unified-account/*) ;;
  *)
    echo "unexpected backup directory: $backup_directory" >&2
    exit 1
    ;;
esac

files=(
  ".env.example"
  "docs/UNIFIED_IDENTITY.md"
  "package.json"
  "prisma/migrations/20260725065000_unified_hechao_identity/migration.sql"
  "prisma/schema.prisma"
  "scripts/import-unified-accounts.mjs"
  "src/app/api/forum/change-password/route.ts"
  "src/app/api/forum/login/route.ts"
  "src/app/api/forum/register/route.ts"
  "src/app/api/forum/reset/route.ts"
  "src/app/api/forum/settings/route.ts"
  "src/components/forum/AuthForm.tsx"
  "src/components/forum/SettingsForm.tsx"
  "src/lib/auth/hechao-identity.ts"
  "src/lib/auth/user.ts"
  "src/lib/session.ts"
  "src/lib/types.ts"
)

for relative_path in "${files[@]}"; do
  test -f "${staged_directory}/${relative_path}"
done
test -f "${backup_directory}/forum-source.tar.gz"
test -d "${site_root}/.next"
test ! -e "$previous_build"
test "$(systemctl is-active "$site_service")" = "active"

rollback_if_needed() {
  exit_code=$?
  if [[ "$deployment_succeeded" == true ]]; then
    return
  fi

  set +e
  systemctl stop "$site_service"
  if [[ -d "$previous_build" ]]; then
    if [[ -d "${site_root}/.next" ]]; then
      mv "${site_root}/.next" \
        "${backup_directory}/forum-next-failed-unified"
    fi
    mv "$previous_build" "${site_root}/.next"
  fi
  tar -xzf "${backup_directory}/forum-source.tar.gz" -C "$site_root"
  systemctl start "$site_service"
  echo "forum deployment failed and the previous build was restored" >&2
  exit "$exit_code"
}
trap rollback_if_needed EXIT

systemctl stop "$site_service"
mv "${site_root}/.next" "$previous_build"

for relative_path in "${files[@]}"; do
  destination="${site_root}/${relative_path}"
  install -d -o ecs-user -g ecs-user -m 0755 "$(dirname "$destination")"
  install -o ecs-user -g ecs-user -m 0644 \
    "${staged_directory}/${relative_path}" "$destination"
done

runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c 'cd "$1" && npm run build' \
  bash "$site_root" > "$build_log" 2>&1
chmod 0600 "$build_log"

systemctl start "$site_service"

site_ready=false
for _ in {1..60}; do
  if curl -fsS --max-time 2 http://127.0.0.1:3000/ >/dev/null 2>&1; then
    site_ready=true
    break
  fi
  sleep 1
done
test "$site_ready" = true

tar -czf "${backup_directory}/forum-next-before-unified.tar.gz" \
  -C "$backup_directory" "$(basename "$previous_build")"
sha256sum "${backup_directory}/forum-next-before-unified.tar.gz" \
  > "${backup_directory}/forum-next-before-unified.tar.gz.sha256"
chmod 0600 \
  "${backup_directory}/forum-next-before-unified.tar.gz" \
  "${backup_directory}/forum-next-before-unified.tar.gz.sha256"

deployment_succeeded=true
echo "forum_unified_account_deployment=ready"

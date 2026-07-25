#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "backup-unified-account-deployment.sh must run as root" >&2
  exit 1
fi

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_directory="/var/backups/hechao-unified-account/${timestamp}"
site_root="/home/ecs-user/hechao"
site_service="hechao.service"
api_environment="/etc/hechao-launcher-api/environment"
api_current="/opt/hechao-launcher-api/current"
database_backup_root="/var/backups/hechao-launcher/database"
site_stopped=false

restart_site_if_needed() {
  if [[ "$site_stopped" == true ]]; then
    systemctl start "$site_service" >/dev/null 2>&1 || true
  fi
}
trap restart_site_if_needed EXIT

test -d "$site_root"
test -f "${site_root}/prisma/dev.db"
test -f "${site_root}/.env"
test -f "$api_environment"
test -L "$api_current"

install -d -o root -g root -m 0700 "$backup_directory"
umask 077

systemctl start hechao-launcher-db-backup.service
test "$(systemctl show hechao-launcher-db-backup.service -p Result --value)" = "success"
database_dump="$(
  find "$database_backup_root" -maxdepth 1 -type f \
    -name 'hechao-launcher-*.dump' -printf '%T@ %p\n' |
    sort -n |
    tail -n 1 |
    cut -d' ' -f2-
)"
test -n "$database_dump"
test -f "${database_dump}.sha256"
sha256sum -c "${database_dump}.sha256" >/dev/null
docker exec -i -u postgres hechao-launcher-postgres \
  pg_restore --list < "$database_dump" >/dev/null
cp -a "$database_dump" "${backup_directory}/launcher-database.dump"
cp -a "${database_dump}.sha256" \
  "${backup_directory}/launcher-database.original.sha256"

api_release="$(readlink -f "$api_current")"
case "$api_release" in
  /opt/hechao-launcher-api/releases/*) ;;
  *)
    echo "unexpected API release target: ${api_release}" >&2
    exit 1
    ;;
esac
tar -czf "${backup_directory}/api-current-release.tar.gz" \
  -C "$(dirname "$api_release")" "$(basename "$api_release")"
cp -a "$api_environment" "${backup_directory}/api.environment"
printf '%s\n' "$api_release" > "${backup_directory}/api-current-target.txt"

systemctl stop "$site_service"
site_stopped=true
cp -a "${site_root}/prisma/dev.db" "${backup_directory}/forum.sqlite"
systemctl start "$site_service"
site_stopped=false

tar -czf "${backup_directory}/forum-source.tar.gz" \
  --exclude='.git' \
  --exclude='.next' \
  --exclude='node_modules' \
  --exclude='.env' \
  --exclude='prisma/dev.db' \
  -C "$site_root" .
cp -a "${site_root}/.env" "${backup_directory}/forum.env"

test -s "${backup_directory}/launcher-database.dump"
test -s "${backup_directory}/forum.sqlite"
tar -tzf "${backup_directory}/api-current-release.tar.gz" >/dev/null
tar -tzf "${backup_directory}/forum-source.tar.gz" >/dev/null

(
  cd "$backup_directory"
  sha256sum \
    api-current-release.tar.gz \
    api.environment \
    api-current-target.txt \
    forum.sqlite \
    forum-source.tar.gz \
    forum.env \
    launcher-database.dump \
    launcher-database.original.sha256 \
    > manifest.sha256
  sha256sum -c manifest.sha256 >/dev/null
)
chmod 0600 "${backup_directory}"/*

curl -fsS http://127.0.0.1:8090/readyz >/dev/null
curl -fsS http://127.0.0.1:3000/ >/dev/null

echo "$backup_directory"

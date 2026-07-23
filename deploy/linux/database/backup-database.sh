#!/usr/bin/env bash
set -euo pipefail

backup_root="/var/backups/hechao-launcher/database"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
temporary_file="${backup_root}/.hechao-launcher-${timestamp}.dump.tmp"
backup_file="${backup_root}/hechao-launcher-${timestamp}.dump"

install -d -o root -g root -m 0700 "$backup_root"
umask 077

docker exec -u postgres hechao-launcher-postgres \
    pg_dump --username=hechao_db_admin --dbname=hechao_launcher --format=custom \
    > "$temporary_file"

test -s "$temporary_file"
mv "$temporary_file" "$backup_file"
chmod 0600 "$backup_file"
sha256sum "$backup_file" > "${backup_file}.sha256"
chmod 0600 "${backup_file}.sha256"

find "$backup_root" -maxdepth 1 -type f \
    \( -name 'hechao-launcher-*.dump' -o -name 'hechao-launcher-*.dump.sha256' \) \
    -mtime +13 -delete

echo "$backup_file"

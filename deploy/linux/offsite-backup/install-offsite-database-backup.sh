#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "install-offsite-database-backup.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 5 ]]; then
  echo "usage: install-offsite-database-backup.sh <binary> <public-key> <runner> <service> <timer>" >&2
  exit 1
fi

binary="$1"
public_key="$2"
runner="$3"
service_file="$4"
timer_file="$5"

test -f "$binary"
test -f "$public_key"
test -f "$runner"
test -f "$service_file"
test -f "$timer_file"
install -d -o root -g root -m 0755 /opt/hechao-backup
install -d -o root -g root -m 0700 \
  /etc/hechao-offsite-backup \
  /var/lib/hechao-offsite-backup \
  /var/backups/hechao-launcher/offsite-staging
test -f /etc/hechao-offsite-backup/environment
test "$(stat -c '%a:%U:%G' /etc/hechao-offsite-backup/environment)" = "600:root:root"
install -o root -g root -m 0555 "$binary" /opt/hechao-backup/Hechao.Backup
install -o root -g root -m 0444 \
  "$public_key" \
  /etc/hechao-offsite-backup/database-recovery-public.pem
install -o root -g root -m 0555 \
  "$runner" \
  /usr/local/sbin/hechao-offsite-database-backup
install -o root -g root -m 0644 \
  "$service_file" \
  /etc/systemd/system/hechao-offsite-database-backup.service
install -o root -g root -m 0644 \
  "$timer_file" \
  /etc/systemd/system/hechao-offsite-database-backup.timer

systemd-analyze verify \
  /etc/systemd/system/hechao-offsite-database-backup.service \
  /etc/systemd/system/hechao-offsite-database-backup.timer
systemctl daemon-reload

echo "Offsite database backup is installed; its timer remains inactive."

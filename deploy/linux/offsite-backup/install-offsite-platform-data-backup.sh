#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "install-offsite-platform-data-backup.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 5 ]]; then
  echo "usage: install-offsite-platform-data-backup.sh <local-runner> <offsite-runner> <verifier> <service> <timer>" >&2
  exit 1
fi

local_runner="$1"
offsite_runner="$2"
verifier="$3"
service_file="$4"
timer_file="$5"

test -f "$local_runner"
test -f "$offsite_runner"
test -f "$verifier"
test -f "$service_file"
test -f "$timer_file"
test -x /opt/hechao-backup/Hechao.Backup
test -f /etc/hechao-offsite-backup/database-recovery-public.pem
test -f /etc/hechao-offsite-backup/environment
test "$(stat -c '%a:%U:%G' /etc/hechao-offsite-backup/environment)" = "600:root:root"

install -d -o root -g root -m 0700 \
  /var/backups/hechao-platform-data/local \
  /var/backups/hechao-platform-data/staging \
  /var/backups/hechao-platform-data/offsite-staging \
  /var/backups/hechao-platform-data/restore-staging \
  /var/lib/hechao-offsite-platform-backup
install -o root -g root -m 0555 \
  "$local_runner" \
  /usr/local/sbin/hechao-platform-data-backup
install -o root -g root -m 0555 \
  "$offsite_runner" \
  /usr/local/sbin/hechao-offsite-platform-data-backup
install -o root -g root -m 0555 \
  "$verifier" \
  /usr/local/sbin/hechao-verify-restored-platform-data
install -o root -g root -m 0644 \
  "$service_file" \
  /etc/systemd/system/hechao-offsite-platform-data-backup.service
install -o root -g root -m 0644 \
  "$timer_file" \
  /etc/systemd/system/hechao-offsite-platform-data-backup.timer

systemd-analyze verify \
  /etc/systemd/system/hechao-offsite-platform-data-backup.service \
  /etc/systemd/system/hechao-offsite-platform-data-backup.timer
systemctl daemon-reload

echo "Offsite platform data backup is installed; its timer remains inactive."

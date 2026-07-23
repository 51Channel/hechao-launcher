#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "install-release.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 4 ]]; then
  echo "usage: install-release.sh <archive> <sha256> <release-id> <service-file>" >&2
  exit 1
fi

archive="$1"
expected_sha256="${2,,}"
release_id="$3"
service_file="$4"
app_root="/opt/hechao-launcher-api"
release_dir="${app_root}/releases/${release_id}"
service_name="hechao-launcher-api.service"

if [[ ! "$release_id" =~ ^[0-9A-Za-z._-]+$ ]]; then
  echo "invalid release id" >&2
  exit 1
fi

actual_sha256="$(sha256sum "$archive" | awk '{print $1}')"
if [[ "$actual_sha256" != "$expected_sha256" ]]; then
  echo "archive checksum mismatch" >&2
  exit 1
fi

while IFS= read -r entry; do
  case "$entry" in
    /*|..|../*|*/../*)
      echo "unsafe archive path: $entry" >&2
      exit 1
      ;;
  esac
done < <(tar -tzf "$archive")

if ! getent group hechao-api >/dev/null; then
  groupadd --system hechao-api
fi
if ! id hechao-api >/dev/null 2>&1; then
  useradd --system --gid hechao-api --home-dir /nonexistent --shell /usr/sbin/nologin --no-create-home hechao-api
fi

install -d -o root -g root -m 0755 "${app_root}/releases"
if [[ -e "$release_dir" ]]; then
  if [[ ! -x "${release_dir}/Hechao.Api" ]]; then
    echo "existing release is incomplete: $release_dir" >&2
    exit 1
  fi
else
  install -d -o root -g root -m 0755 "$release_dir"
  tar -xzf "$archive" -C "$release_dir"
  chown -R root:root "$release_dir"
  find "$release_dir" -type d -exec chmod 0755 {} +
  find "$release_dir" -type f -exec chmod 0444 {} +
  chmod 0555 "${release_dir}/Hechao.Api"
fi

install -o root -g root -m 0644 "$service_file" "/etc/systemd/system/${service_name}"

previous_target="$(readlink -f "${app_root}/current" 2>/dev/null || true)"
next_link="${app_root}/.current-${release_id}"
rm -f "$next_link"
ln -s "$release_dir" "$next_link"
mv -Tf "$next_link" "${app_root}/current"

if ! systemd-analyze verify "/etc/systemd/system/${service_name}"; then
  rm -f "${app_root}/current"
  if [[ -n "$previous_target" ]]; then
    ln -s "$previous_target" "${app_root}/current"
  fi
  exit 1
fi

systemctl daemon-reload
systemctl enable "$service_name"
systemctl restart "$service_name"

healthy=false
for _ in {1..30}; do
  if curl --fail --silent --show-error --max-time 2 http://127.0.0.1:8090/readyz >/dev/null 2>&1; then
    healthy=true
    break
  fi
  sleep 1
done

if [[ "$healthy" != true ]]; then
  echo "new API release failed readiness checks; rolling back" >&2
  journalctl -u "$service_name" -n 40 --no-pager >&2 || true

  if [[ -n "$previous_target" && -x "${previous_target}/Hechao.Api" ]]; then
    rollback_link="${app_root}/.rollback-${release_id}"
    rm -f "$rollback_link"
    ln -s "$previous_target" "$rollback_link"
    mv -Tf "$rollback_link" "${app_root}/current"
    systemctl restart "$service_name"
  fi

  exit 1
fi

echo "API release ${release_id} is ready"

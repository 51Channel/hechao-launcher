#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "install-package-publisher.sh must run as root" >&2
  exit 1
fi

start_service=false
if [[ "$#" -eq 6 && "$6" == "--start" ]]; then
  start_service=true
elif [[ "$#" -ne 5 ]]; then
  echo "usage: install-package-publisher.sh <binary> <config> <sha256> <release-id> <service-file> [--start]" >&2
  exit 1
fi

binary="$1"
config="$2"
expected_sha256="${3,,}"
release_id="$4"
service_file="$5"
app_root="/opt/hechao-package-publisher"
release_dir="${app_root}/releases/${release_id}"
config_root="/etc/hechao-package-publisher"
config_path="${config_root}/agent.json"
service_name="hechao-package-publisher.service"
service_path="/etc/systemd/system/${service_name}"
credential_root="/etc/credstore.encrypted/hechao-package-publisher"

if [[ ! "$release_id" =~ ^[0-9A-Za-z._-]+$ ]] ||
   [[ ! "$expected_sha256" =~ ^[0-9a-f]{64}$ ]]; then
  echo "invalid release id or checksum" >&2
  exit 1
fi

for path in "$binary" "$config" "$service_file"; do
  if [[ ! -f "$path" || -L "$path" ]]; then
    echo "missing or unsafe input: $path" >&2
    exit 1
  fi
done

actual_sha256="$(sha256sum "$binary" | awk '{print $1}')"
if [[ "$actual_sha256" != "$expected_sha256" ]]; then
  echo "publisher checksum mismatch" >&2
  exit 1
fi

python3 - "$config" <<'PY'
import json
import pathlib
import sys
import urllib.parse

path = pathlib.Path(sys.argv[1])
data = json.loads(path.read_text(encoding="utf-8"))
if data.get("secretStorage") != "systemd-credentials":
    raise SystemExit("publisher config must use systemd-credentials")
expected = {
    "tokenPath": "publisher-token",
    "signingKeyPath": "distribution-signing-key",
    "ossCredentialPath": "oss-publisher-credential",
}
for key, value in expected.items():
    if data.get(key) != value:
        raise SystemExit(f"publisher config has invalid {key}")
for forbidden in ("token", "accessKeyId", "accessKeySecret", "privateKey"):
    if forbidden in data:
        raise SystemExit(f"publisher config contains forbidden field {forbidden}")
public_base = urllib.parse.urlsplit(data.get("publicObjectBaseUrl", ""))
if (public_base.scheme != "https" or not public_base.hostname or
        public_base.username or public_base.password or
        public_base.path not in ("", "/") or public_base.query or
        public_base.fragment):
    raise SystemExit("publisher config has invalid publicObjectBaseUrl")
PY

for credential in \
  publisher-token.cred \
  distribution-signing-key.cred \
  oss-publisher-credential.cred; do
  path="${credential_root}/${credential}"
  if [[ ! -f "$path" || -L "$path" ]]; then
    echo "missing encrypted credential: $path" >&2
    exit 1
  fi
  if [[ "$(stat -c '%U:%G %a' "$path")" != "root:root 600" ]]; then
    echo "encrypted credential permissions are invalid: $path" >&2
    exit 1
  fi
done

if ! getent group hechao-publisher >/dev/null; then
  groupadd --system hechao-publisher
fi
if ! id hechao-publisher >/dev/null 2>&1; then
  useradd --system --gid hechao-publisher --home-dir /nonexistent \
    --shell /usr/sbin/nologin --no-create-home hechao-publisher
fi

install -d -o root -g root -m 0755 "${app_root}/releases"
install -d -o root -g hechao-publisher -m 0750 "$config_root"
if [[ -e "$release_dir" ]]; then
  if [[ ! -x "${release_dir}/Hechao.Publisher" ]] ||
     [[ "$(sha256sum "${release_dir}/Hechao.Publisher" | awk '{print $1}')" != "$expected_sha256" ]]; then
    echo "existing publisher release is incomplete or different" >&2
    exit 1
  fi
else
  install -d -o root -g root -m 0755 "$release_dir"
  install -o root -g root -m 0555 "$binary" "${release_dir}/Hechao.Publisher"
fi

backup_root="/var/backups/hechao-package-publisher/$(date -u +%Y%m%dT%H%M%SZ)"
install -d -o root -g root -m 0700 "$backup_root"
previous_target="$(readlink -f "${app_root}/current" 2>/dev/null || true)"
was_active=false
was_enabled=false
if systemctl is-active --quiet "$service_name"; then
  was_active=true
fi
if systemctl is-enabled --quiet "$service_name"; then
  was_enabled=true
fi
if [[ -f "$config_path" ]]; then
  cp --preserve=mode,ownership,timestamps "$config_path" "$backup_root/agent.json"
fi
if [[ -f "$service_path" ]]; then
  cp --preserve=mode,ownership,timestamps "$service_path" "$backup_root/${service_name}"
fi
printf '%s\n' "$previous_target" >"$backup_root/previous-target.txt"

rollback() {
  local status=$?
  trap - ERR
  if [[ -n "$previous_target" && -x "${previous_target}/Hechao.Publisher" ]]; then
    rollback_link="${app_root}/.rollback-${release_id}"
    ln -sfn "$previous_target" "$rollback_link"
    mv -Tf "$rollback_link" "${app_root}/current"
  else
    unlink "${app_root}/current" 2>/dev/null || true
  fi
  if [[ -f "$backup_root/agent.json" ]]; then
    install -o root -g hechao-publisher -m 0640 \
      "$backup_root/agent.json" "$config_path"
  else
    rm -f "$config_path"
  fi
  if [[ -f "$backup_root/${service_name}" ]]; then
    install -o root -g root -m 0644 \
      "$backup_root/${service_name}" "$service_path"
  else
    rm -f "$service_path"
  fi
  systemctl daemon-reload || true
  if [[ "$was_active" == true ]]; then
    systemctl restart "$service_name" || true
  else
    systemctl stop "$service_name" || true
  fi
  if [[ "$was_enabled" != true ]]; then
    systemctl disable "$service_name" || true
  fi
  echo "publisher installation failed and previous service state was restored" >&2
  exit "$status"
}
trap rollback ERR

install -o root -g hechao-publisher -m 0640 "$config" "$config_path"
install -o root -g root -m 0644 "$service_file" "$service_path"
next_link="${app_root}/.current-${release_id}"
ln -sfn "$release_dir" "$next_link"
mv -Tf "$next_link" "${app_root}/current"

systemd-analyze verify "$service_path"
systemctl daemon-reload
if [[ "$start_service" == true ]]; then
  systemctl enable "$service_name"
  systemctl restart "$service_name"
  sleep 2
  systemctl is-active --quiet "$service_name"
fi

trap - ERR
echo "publisher release ${release_id} installed start=${start_service}"

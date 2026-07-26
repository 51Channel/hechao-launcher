#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "smoke-test-forum-session-revocation.sh must run as root" >&2
  exit 1
fi
if [[ "$#" -lt 3 || "$#" -gt 4 ]]; then
  echo \
    "usage: smoke-test-forum-session-revocation.sh <overlay-archive> <sha256> <forum-source-backup> [port]" \
    >&2
  exit 1
fi

overlay_archive="$1"
expected_sha256="${2,,}"
source_backup="$3"
port="${4:-13001}"
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
work_root="/tmp/hechao-forum-revocation-smoke-${run_id}"
log_file="${work_root}/next.log"
unit_name="hechao-forum-revocation-smoke-${run_id}"
unit_started=false

fail() {
  echo "FAIL: $*" >&2
  return 1
}

cleanup() {
  exit_code=$?
  trap - EXIT
  if [[ "$unit_started" == true ]]; then
    systemctl stop "${unit_name}.service" >/dev/null 2>&1 || true
    systemctl reset-failed "${unit_name}.service" >/dev/null 2>&1 || true
  fi
  if [[ "$exit_code" -ne 0 && -f "$log_file" ]]; then
    tail -n 80 "$log_file" >&2 || true
  fi
  if [[ "$exit_code" -ne 0 && -f "${work_root}/build.log" ]]; then
    echo "Build log tail:" >&2
    tail -n 80 "${work_root}/build.log" >&2 || true
  fi
  case "$work_root" in
    /tmp/hechao-forum-revocation-smoke-*)
      rm -rf -- "$work_root"
      ;;
    *)
      echo "refusing to remove unexpected test directory" >&2
      exit_code=1
      ;;
  esac
  exit "$exit_code"
}
trap cleanup EXIT

for tool in curl npm sha256sum systemctl tar; do
  command -v "$tool" >/dev/null || fail "required tool is missing: $tool"
done
npm_path="$(command -v npm)"
[[ -f "$overlay_archive" ]] || fail "overlay archive does not exist"
[[ -f "$source_backup" ]] || fail "forum source backup does not exist"
[[ "$port" =~ ^[0-9]+$ ]] || fail "port must be numeric"
((port >= 1024 && port <= 65535)) || fail "port is outside the allowed range"
[[ "$(sha256sum "$overlay_archive" | awk '{print $1}')" == "$expected_sha256" ]] ||
  fail "overlay archive checksum mismatch"
tar -tzf "$source_backup" >/dev/null

if ss -H -ltn "sport = :${port}" | grep -q .; then
  fail "test port ${port} is already in use"
fi

install -d -o ecs-user -g ecs-user -m 0700 "$work_root"
while IFS= read -r entry; do
  case "$entry" in
    /*|..|../*|*/../*)
      fail "unsafe overlay path: $entry"
      ;;
  esac
done < <(tar -tzf "$overlay_archive")

tar -xzf "$source_backup" -C "$work_root"
tar -xzf "$overlay_archive" -C "$work_root"
install -o ecs-user -g ecs-user -m 0600 \
  /home/ecs-user/hechao/.env "${work_root}/.env"
install -o ecs-user -g ecs-user -m 0600 \
  /home/ecs-user/hechao/prisma/dev.db "${work_root}/prisma/dev.db"
cp -al /home/ecs-user/hechao/node_modules "${work_root}/node_modules"
chown -R ecs-user:ecs-user "$work_root"

runuser -u ecs-user -- env HOME=/home/ecs-user \
  bash -c 'cd "$1" && npx prisma migrate deploy && npm run build' \
  bash "$work_root" > "${work_root}/build.log" 2>&1

install -o ecs-user -g ecs-user -m 0600 /dev/null "$log_file"
systemd-run \
  --unit="$unit_name" \
  --uid=ecs-user \
  --gid=ecs-user \
  --property="WorkingDirectory=${work_root}" \
  --property="StandardOutput=append:${log_file}" \
  --property="StandardError=append:${log_file}" \
  --collect \
  "$npm_path" start -- --hostname 127.0.0.1 --port "$port" \
  >/dev/null
unit_started=true

ready=false
for _ in {1..60}; do
  if curl -fsS --max-time 2 "http://127.0.0.1:${port}/" >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 1
done
[[ "$ready" == true ]] || fail "isolated forum did not become ready"

unauthorized="$(
  curl --silent --show-error \
    --output /dev/null \
    --write-out '%{http_code}' \
    --request POST \
    --header 'Content-Type: application/json' \
    --data '{}' \
    "http://127.0.0.1:${port}/api/internal/hechao/session-revoke"
)"
[[ "$unauthorized" == "404" ]] ||
  fail "unauthorized internal route returned ${unauthorized}, expected 404"

token="$(
  sed -n 's/^HECHAO_SESSION_REVOCATION_TOKEN=//p' "${work_root}/.env" |
    tail -n 1
)"
[[ "${#token}" -ge 32 && "${#token}" -le 256 ]] ||
  fail "forum session revocation token is invalid"
authorized_validation="$(
  curl --silent --show-error \
    --output /dev/null \
    --write-out '%{http_code}' \
    --request POST \
    --header 'Content-Type: application/json' \
    --header "X-Hechao-Session-Token: ${token}" \
    --data '{}' \
    "http://127.0.0.1:${port}/api/internal/hechao/session-revoke"
)"
unset token
[[ "$authorized_validation" == "400" ]] ||
  fail \
    "authorized internal route returned ${authorized_validation}, expected 400"

echo "PASS: isolated forum session-revocation overlay"
echo "Evidence: build=passed, unauthorized=404, authorized-validation=400"

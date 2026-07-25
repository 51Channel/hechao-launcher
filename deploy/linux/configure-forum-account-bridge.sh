#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-forum-account-bridge.sh must run as root" >&2
  exit 1
fi

api_environment="/etc/hechao-launcher-api/environment"
forum_environment="/home/ecs-user/hechao/.env"
api_temporary="$(mktemp)"
forum_temporary="$(mktemp)"
trap 'rm -f "$api_temporary" "$forum_temporary"' EXIT

test -f "$api_environment"
test -f "$forum_environment"
command -v openssl >/dev/null

bridge_token="$(openssl rand -hex 32)"
bridge_token_sha256="$(
  printf '%s' "$bridge_token" | sha256sum | awk '{print $1}'
)"

grep -v '^ForumAccountBridge__' "$api_environment" > "$api_temporary" || true
cat >> "$api_temporary" <<EOF
ForumAccountBridge__InternalTokenSha256=$bridge_token_sha256
ForumAccountBridge__AllowLegacyImport=true
EOF

grep -v -E '^(HECHAO_IDENTITY_API_URL=|HECHAO_FORUM_BRIDGE_TOKEN=)' \
  "$forum_environment" > "$forum_temporary" || true
cat >> "$forum_temporary" <<EOF
HECHAO_IDENTITY_API_URL=http://127.0.0.1:8090/
HECHAO_FORUM_BRIDGE_TOKEN=$bridge_token
EOF

install -o root -g root -m 0600 "$api_temporary" "$api_environment"
install -o ecs-user -g ecs-user -m 0600 \
  "$forum_temporary" "$forum_environment"

stored_token="$(
  sed -n 's/^HECHAO_FORUM_BRIDGE_TOKEN=//p' "$forum_environment"
)"
stored_digest="$(
  sed -n 's/^ForumAccountBridge__InternalTokenSha256=//p' "$api_environment"
)"
calculated_digest="$(
  printf '%s' "$stored_token" | sha256sum | awk '{print $1}'
)"

test -n "$stored_token"
test "$stored_digest" = "$calculated_digest"
test "$(
  grep -c '^HECHAO_FORUM_BRIDGE_TOKEN=' "$forum_environment"
)" -eq 1
test "$(
  grep -c '^ForumAccountBridge__InternalTokenSha256=' "$api_environment"
)" -eq 1

unset bridge_token stored_token
echo "forum_account_bridge_configuration=ready"
echo "legacy_import=enabled"
echo "services_restart=not_performed"

#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-forum-session-revocation.sh must run as root" >&2
  exit 1
fi

umask 077
api_environment="/etc/hechao-launcher-api/environment"
forum_environment="/home/ecs-user/hechao/.env"
api_temporary="$(mktemp)"
forum_temporary="$(mktemp)"
trap 'rm -f "$api_temporary" "$forum_temporary"' EXIT

test -f "$api_environment"
test -f "$forum_environment"
command -v openssl >/dev/null

token="$(
  sed -n 's/^HECHAO_SESSION_REVOCATION_TOKEN=//p' "$forum_environment" |
    tail -n 1
)"
if [[ "${#token}" -lt 32 || "${#token}" -gt 256 ]]; then
  token="$(openssl rand -hex 32)"
fi

grep -v '^ForumSessionRevocation__' \
  "$api_environment" > "$api_temporary" || true
cat >> "$api_temporary" <<EOF
ForumSessionRevocation__Enabled=true
ForumSessionRevocation__BaseUrl=http://127.0.0.1:3000/
ForumSessionRevocation__InternalToken=$token
ForumSessionRevocation__DeliveryIntervalSeconds=5
ForumSessionRevocation__RequestTimeoutSeconds=5
ForumSessionRevocation__LeaseSeconds=30
ForumSessionRevocation__BatchSize=20
EOF

grep -v '^HECHAO_SESSION_REVOCATION_TOKEN=' \
  "$forum_environment" > "$forum_temporary" || true
cat >> "$forum_temporary" <<EOF
HECHAO_SESSION_REVOCATION_TOKEN=$token
EOF

install -o root -g root -m 0600 "$api_temporary" "$api_environment"
install -o ecs-user -g ecs-user -m 0600 \
  "$forum_temporary" "$forum_environment"

api_token="$(
  sed -n 's/^ForumSessionRevocation__InternalToken=//p' \
    "$api_environment"
)"
forum_token="$(
  sed -n 's/^HECHAO_SESSION_REVOCATION_TOKEN=//p' "$forum_environment"
)"
test -n "$api_token"
test "$api_token" = "$forum_token"
test "$(
  grep -c '^ForumSessionRevocation__InternalToken=' "$api_environment"
)" -eq 1
test "$(
  grep -c '^HECHAO_SESSION_REVOCATION_TOKEN=' "$forum_environment"
)" -eq 1

unset token api_token forum_token
echo "forum_session_revocation_configuration=ready"
echo "services_restart=not_performed"

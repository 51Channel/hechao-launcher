#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "configure-website-activity-bridge.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 1 ]]; then
  echo "usage: configure-website-activity-bridge.sh <actor-user-id>" >&2
  exit 1
fi

actor_user_id="${1,,}"
if [[ ! "$actor_user_id" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$ ]]; then
  echo "actor-user-id must be a canonical UUID" >&2
  exit 1
fi

api_environment="/etc/hechao-launcher-api/environment"
website_environment="/home/ecs-user/hechao/.env"
api_temporary="$(mktemp)"
website_temporary="$(mktemp)"
trap 'rm -f "$api_temporary" "$website_temporary"' EXIT

test -f "$api_environment"
test -f "$website_environment"
command -v openssl >/dev/null
command -v sha256sum >/dev/null

bridge_token="$(openssl rand -hex 32)"
bridge_token_sha256="$(
  printf '%s' "$bridge_token" | sha256sum | awk '{print $1}'
)"

grep -v '^WebsiteActivityBridge__' "$api_environment" > "$api_temporary" || true
cat >> "$api_temporary" <<EOF
WebsiteActivityBridge__InternalTokenSha256=$bridge_token_sha256
WebsiteActivityBridge__ActorUserId=$actor_user_id
EOF

grep -v '^HECHAO_ACTIVITY_BRIDGE_TOKEN=' \
  "$website_environment" > "$website_temporary" || true
cat >> "$website_temporary" <<EOF
HECHAO_ACTIVITY_BRIDGE_TOKEN=$bridge_token
EOF

install -o root -g root -m 0600 "$api_temporary" "$api_environment"
install -o ecs-user -g ecs-user -m 0600 \
  "$website_temporary" "$website_environment"

stored_token="$(
  sed -n 's/^HECHAO_ACTIVITY_BRIDGE_TOKEN=//p' "$website_environment"
)"
stored_digest="$(
  sed -n 's/^WebsiteActivityBridge__InternalTokenSha256=//p' "$api_environment"
)"
stored_actor="$(
  sed -n 's/^WebsiteActivityBridge__ActorUserId=//p' "$api_environment"
)"
calculated_digest="$(
  printf '%s' "$stored_token" | sha256sum | awk '{print $1}'
)"

test "${#stored_token}" -ge 32
test "${#stored_token}" -le 256
test "$stored_digest" = "$calculated_digest"
test "$stored_actor" = "$actor_user_id"
test "$(grep -c '^HECHAO_ACTIVITY_BRIDGE_TOKEN=' "$website_environment")" -eq 1
test "$(grep -c '^WebsiteActivityBridge__InternalTokenSha256=' "$api_environment")" -eq 1
test "$(grep -c '^WebsiteActivityBridge__ActorUserId=' "$api_environment")" -eq 1

unset bridge_token stored_token
echo "website_activity_bridge_configuration=ready"
echo "actor_user_id_configured=true"
echo "services_restart=not_performed"

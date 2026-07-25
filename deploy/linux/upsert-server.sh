#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "upsert-server.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -lt 11 ]] || [[ "$#" -gt 12 ]]; then
  echo "usage: upsert-server.sh <id> <display-name> <short-name> <icon-glyph> <status> <max-players> <minecraft-version> <loader> <minimum-tier> <client-profile-id> <velocity-target> [sort-order]" >&2
  exit 1
fi

server_id="$1"
display_name="$2"
short_name="$3"
icon_glyph="$4"
status="$5"
max_players="$6"
minecraft_version="$7"
loader="$8"
minimum_tier="$9"
client_profile_id="${10}"
velocity_target="${11}"
sort_order="${12:-0}"
postgres_container="hechao-launcher-postgres"

if [[ ! "$server_id" =~ ^[a-z0-9][a-z0-9._-]{1,63}$ ]] ||
   [[ ! "$client_profile_id" =~ ^[a-z0-9][a-z0-9._-]{1,63}$ ]] ||
   [[ ! "$velocity_target" =~ ^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$ ]] ||
   [[ ! "$max_players" =~ ^[1-9][0-9]*$ ]] ||
   [[ ! "$sort_order" =~ ^-?[0-9]+$ ]] ||
   [[ ! "$status" =~ ^(Online|Maintenance|Closed)$ ]] ||
   [[ ! "$loader" =~ ^(Vanilla|Paper|NeoForge|Fabric|Forge)$ ]] ||
   [[ ! "$minimum_tier" =~ ^(Member|Participant|Collaborator|Administrator)$ ]] ||
   [[ "${#display_name}" -lt 1 || "${#display_name}" -gt 80 ]] ||
   [[ "${#short_name}" -lt 1 || "${#short_name}" -gt 12 ]] ||
   [[ "${#icon_glyph}" -lt 1 || "${#icon_glyph}" -gt 12 ]] ||
   [[ "${#minecraft_version}" -lt 1 || "${#minecraft_version}" -gt 40 ]] ||
   [[ "$display_name$short_name$icon_glyph$minecraft_version" =~ [[:cntrl:]] ]]; then
  echo "invalid server catalog arguments" >&2
  exit 1
fi

escape_sql_text() {
  local value="$1"
  printf "%s" "${value//\'/\'\'}"
}

display_name_sql="$(escape_sql_text "$display_name")"
short_name_sql="$(escape_sql_text "$short_name")"
icon_glyph_sql="$(escape_sql_text "$icon_glyph")"
minecraft_version_sql="$(escape_sql_text "$minecraft_version")"

sql="BEGIN;
INSERT INTO launcher.servers
    (id, display_name, short_name, icon_glyph, status, online_players,
     max_players, minecraft_version, loader, minimum_tier,
     client_profile_id, velocity_target, sort_order, is_visible)
VALUES
    ('${server_id}', '${display_name_sql}', '${short_name_sql}', '${icon_glyph_sql}',
     '${status}', 0, ${max_players}, '${minecraft_version_sql}', '${loader}',
     '${minimum_tier}', '${client_profile_id}', '${velocity_target}',
     ${sort_order}, true)
ON CONFLICT (id) DO UPDATE
SET display_name = EXCLUDED.display_name,
    short_name = EXCLUDED.short_name,
    icon_glyph = EXCLUDED.icon_glyph,
    status = EXCLUDED.status,
    online_players = LEAST(launcher.servers.online_players, EXCLUDED.max_players),
    max_players = EXCLUDED.max_players,
    minecraft_version = EXCLUDED.minecraft_version,
    loader = EXCLUDED.loader,
    minimum_tier = EXCLUDED.minimum_tier,
    client_profile_id = EXCLUDED.client_profile_id,
    velocity_target = EXCLUDED.velocity_target,
    sort_order = EXCLUDED.sort_order,
    is_visible = true,
    updated_at = now();
INSERT INTO launcher.audit_logs
    (action, target_type, target_id, after_data)
VALUES
    ('catalog.server.ops_upsert', 'server', '${server_id}',
     jsonb_build_object(
         'displayName', '${display_name_sql}',
         'status', '${status}',
         'maxPlayers', ${max_players},
         'minecraftVersion', '${minecraft_version_sql}',
         'loader', '${loader}',
         'minimumTier', '${minimum_tier}',
         'clientProfileId', '${client_profile_id}',
         'velocityTarget', '${velocity_target}',
         'sortOrder', ${sort_order}));
COMMIT;
SELECT id, display_name, status, max_players, minecraft_version, loader,
       minimum_tier, client_profile_id, velocity_target, sort_order
FROM launcher.servers
WHERE id = '${server_id}';"

docker exec "$postgres_container" sh -lc \
  'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d hechao_launcher -c "$1"' \
  sh "$sql"

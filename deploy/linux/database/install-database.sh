#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
    echo "install-database.sh must run as root" >&2
    exit 1
fi

if [[ "$#" -ne 5 ]]; then
    echo "usage: install-database.sh <compose-file> <init-script> <backup-script> <backup-service> <backup-timer>" >&2
    exit 1
fi

compose_source="$1"
init_source="$2"
backup_source="$3"
backup_service_source="$4"
backup_timer_source="$5"
data_root="/opt/hechao-launcher-database"
environment_file="${data_root}/.env"
api_environment_dir="/etc/hechao-launcher-api"
api_environment_file="${api_environment_dir}/environment"

test -f "$compose_source"
test -f "$init_source"
test -f "$backup_source"
test -f "$backup_service_source"
test -f "$backup_timer_source"

install -d -o root -g root -m 0700 "$data_root"
install -d -o root -g root -m 0700 "${data_root}/init"
install -o root -g root -m 0600 "$compose_source" "${data_root}/compose.yaml"
install -o root -g root -m 0755 "$init_source" "${data_root}/init/001-create-app-db.sh"
install -o root -g root -m 0755 "$backup_source" /usr/local/sbin/hechao-launcher-db-backup
install -o root -g root -m 0644 "$backup_service_source" /etc/systemd/system/hechao-launcher-db-backup.service
install -o root -g root -m 0644 "$backup_timer_source" /etc/systemd/system/hechao-launcher-db-backup.timer

if [[ ! -f "$environment_file" ]]; then
    admin_password="$(openssl rand -hex 32)"
    app_password="$(openssl rand -hex 32)"
    umask 077
    printf 'POSTGRES_ADMIN_PASSWORD=%s\nHECHAO_APP_PASSWORD=%s\n' \
        "$admin_password" "$app_password" > "$environment_file"
fi

chmod 0600 "$environment_file"
# shellcheck disable=SC1090
source "$environment_file"
: "${POSTGRES_ADMIN_PASSWORD:?missing database admin password}"
: "${HECHAO_APP_PASSWORD:?missing application database password}"

install -d -o root -g root -m 0750 "$api_environment_dir"
umask 077
printf '%s\n' \
    "ConnectionStrings__LauncherDatabase=Host=127.0.0.1;Port=5433;Database=hechao_launcher;Username=hechao_api;Password=${HECHAO_APP_PASSWORD};Pooling=true" \
    > "$api_environment_file"
chown root:root "$api_environment_file"
chmod 0600 "$api_environment_file"

docker compose \
    --project-directory "$data_root" \
    --env-file "$environment_file" \
    -f "${data_root}/compose.yaml" \
    up -d

for _ in {1..30}; do
    health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' hechao-launcher-postgres 2>/dev/null || true)"
    if [[ "$health" == "healthy" ]]; then
        break
    fi
    sleep 2
done

if [[ "${health:-}" != "healthy" ]]; then
    echo "launcher database did not become healthy" >&2
    docker logs --tail 80 hechao-launcher-postgres >&2 || true
    exit 1
fi

listen_address="$(ss -lntH '( sport = :5433 )' | awk '{print $4}')"
if [[ "$listen_address" != "127.0.0.1:5433" ]]; then
    echo "unexpected PostgreSQL listen address: ${listen_address:-missing}" >&2
    exit 1
fi

systemctl daemon-reload
systemctl enable --now hechao-launcher-db-backup.timer

echo "launcher database is healthy on 127.0.0.1:5433"

#!/usr/bin/env bash
set -Eeuo pipefail

if [[ ${EUID:-$(id -u)} -ne 0 ]]; then
    printf 'Run this script as root.\n' >&2
    exit 1
fi

source_dir=${1:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)}
format_source="$source_dir/00-hechao-privacy-log.conf"
snippet_source="$source_dir/hechao-privacy-access-log.conf"
launcher_source="$source_dir/hechao-launcher.conf"

format_target=/etc/nginx/conf.d/00-hechao-privacy-log.conf
snippet_target=/etc/nginx/snippets/hechao-privacy-access-log.conf
site_target=/etc/nginx/sites-available/hechao.conf
launcher_target=/etc/nginx/sites-available/hechao-launcher.conf
include_line='    include /etc/nginx/snippets/hechao-privacy-access-log.conf;'

for required in "$format_source" "$snippet_source" "$launcher_source" "$site_target" "$launcher_target"; do
    if [[ ! -f "$required" ]]; then
        printf 'Required file is missing: %s\n' "$required" >&2
        exit 1
    fi
done

stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup_dir="/var/backups/hechao-nginx-privacy/$stamp"
stage_dir=$(mktemp -d /root/hechao-nginx-privacy.XXXXXX)
install -d -m 700 "$backup_dir"

targets=(
    "$format_target"
    "$snippet_target"
    "$site_target"
    "$launcher_target"
)

for target in "${targets[@]}"; do
    if [[ -e "$target" ]]; then
        cp -a "$target" "$backup_dir/$(basename -- "$target")"
    fi
done

sha256sum "$backup_dir"/* > "$backup_dir/SHA256SUMS"
chmod 600 "$backup_dir"/*

site_include_count=$(grep -Fc "$include_line" "$site_target" || true)
if [[ "$site_include_count" -eq 0 ]]; then
    awk -v include_line="$include_line" '
        { print }
        /^[[:space:]]*server_name (hechao[.]world|api[.]hechao[.]world)/ {
            print include_line
        }
    ' "$site_target" > "$stage_dir/hechao.conf"
elif [[ "$site_include_count" -eq 2 ]]; then
    cp "$site_target" "$stage_dir/hechao.conf"
else
    printf 'Unexpected privacy include count in %s: %s\n' "$site_target" "$site_include_count" >&2
    exit 1
fi

cp "$launcher_source" "$stage_dir/hechao-launcher.conf"

if [[ $(grep -Fc "$include_line" "$stage_dir/hechao.conf") -ne 2 ]]; then
    printf 'Expected two privacy logging includes in the forum/API site.\n' >&2
    exit 1
fi

if [[ $(grep -Fc "$include_line" "$stage_dir/hechao-launcher.conf") -ne 3 ]]; then
    printf 'Expected three privacy logging includes in the launcher sites.\n' >&2
    exit 1
fi

if [[ $(grep -c '^[[:space:]]*proxy_hide_header ' "$stage_dir/hechao-launcher.conf") -ne 6 ]]; then
    printf 'Expected six upstream security-header suppression rules.\n' >&2
    exit 1
fi

rollback() {
    local target backup
    for target in "${targets[@]}"; do
        backup="$backup_dir/$(basename -- "$target")"
        if [[ -e "$backup" ]]; then
            cp -a "$backup" "$target"
        else
            rm -f "$target"
        fi
    done
    nginx -t
}

install -m 644 "$format_source" "$format_target"
install -m 644 "$snippet_source" "$snippet_target"
install -m 644 "$stage_dir/hechao.conf" "$site_target"
install -m 644 "$stage_dir/hechao-launcher.conf" "$launcher_target"

if ! nginx -t; then
    printf 'Nginx validation failed; restoring %s\n' "$backup_dir" >&2
    rollback
    exit 1
fi

if ! systemctl reload nginx; then
    printf 'Nginx reload failed; restoring %s\n' "$backup_dir" >&2
    rollback
    systemctl reload nginx
    exit 1
fi

rm -rf "$stage_dir"
printf 'backup_dir=%s\n' "$backup_dir"
printf 'status=deployed\n'

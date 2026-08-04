#!/usr/bin/env bash
set -Eeuo pipefail

config=${1:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/hechao-launcher.conf}

if [[ ! -f "$config" ]]; then
    printf 'Nginx launcher config is missing: %s\n' "$config" >&2
    exit 1
fi

awk '
function verify(text, label, all_limits, exact_limits, copy) {
    copy = text
    all_limits = gsub(/client_max_body_size[[:space:]]+[^;]+;/, "", copy)
    copy = text
    exact_limits = gsub(/client_max_body_size[[:space:]]+10m[[:space:]]*;/, "", copy)

    if (all_limits != 1 || exact_limits != 1) {
        printf "%s HTTPS server must contain exactly one client_max_body_size 10m directive.\n", label > "/dev/stderr"
        errors++
    }
}

function finish_server() {
    if (block ~ /server_name[[:space:]]+launcher-api[.]hechao[.]world[[:space:]]*;/) {
        api_servers++
        verify(block, "launcher-api.hechao.world")
    }

    if (block ~ /server_name[[:space:]]+admin[.]hechao[.]world[[:space:]]*;/) {
        admin_servers++
        verify(block, "admin.hechao.world")
    }
}

/^[[:space:]]*server[[:space:]]*\{/ && !in_server {
    in_server = 1
    depth = 0
    block = ""
}

in_server {
    block = block $0 "\n"
    line = $0
    depth += gsub(/\{/, "{", line)
    depth -= gsub(/\}/, "}", line)

    if (depth == 0) {
        finish_server()
        in_server = 0
        block = ""
    }
}

END {
    if (in_server) {
        print "Unclosed server block in launcher Nginx config." > "/dev/stderr"
        errors++
    }
    if (api_servers != 1) {
        printf "Expected one launcher-api.hechao.world HTTPS server, found %d.\n", api_servers > "/dev/stderr"
        errors++
    }
    if (admin_servers != 1) {
        printf "Expected one admin.hechao.world HTTPS server, found %d.\n", admin_servers > "/dev/stderr"
        errors++
    }
    exit errors != 0
}
' "$config"

printf 'launcher_upload_limit_contract=ok\n'

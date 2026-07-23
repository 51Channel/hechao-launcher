#!/bin/sh
set -eu

if [ -z "${HECHAO_APP_PASSWORD:-}" ]; then
    echo "HECHAO_APP_PASSWORD is required" >&2
    exit 1
fi

psql --set=ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname postgres \
    --set=app_password="$HECHAO_APP_PASSWORD" <<'SQL'
CREATE ROLE hechao_api
    LOGIN
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOINHERIT
    PASSWORD :'app_password';

CREATE DATABASE hechao_launcher
    OWNER hechao_api
    ENCODING 'UTF8'
    TEMPLATE template0;

REVOKE ALL ON DATABASE hechao_launcher FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE hechao_launcher TO hechao_api;
SQL

psql --set=ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname hechao_launcher <<'SQL'
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO hechao_api;
GRANT USAGE, CREATE ON SCHEMA public TO hechao_api;
SQL

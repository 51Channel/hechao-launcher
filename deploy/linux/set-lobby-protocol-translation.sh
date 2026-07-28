#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "set-lobby-protocol-translation.sh must run as root" >&2
  exit 1
fi

if [[ "$#" -ne 3 ]] || [[ "$3" != "--confirm-production" ]]; then
  echo "usage: set-lobby-protocol-translation.sh <true|false> <expected-current> --confirm-production" >&2
  exit 1
fi

desired="$1"
expected_current="$2"
postgres_container="hechao-launcher-postgres"

if [[ ! "$desired" =~ ^(true|false)$ ]] ||
   [[ ! "$expected_current" =~ ^(true|false)$ ]]; then
  echo "desired and expected-current must be true or false" >&2
  exit 1
fi

sql="BEGIN;
SELECT pg_advisory_xact_lock(hashtext('hechao:lobby-protocol-translation'));

DO \$operation\$
DECLARE
    current_value boolean;
    conflicting_targets integer;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'launcher'
          AND table_name = 'servers'
          AND column_name = 'allow_protocol_translation'
    ) THEN
        RAISE EXCEPTION 'migration 018 is not installed';
    END IF;

    SELECT allow_protocol_translation
    INTO current_value
    FROM launcher.servers
    WHERE id = 'lobby'
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'lobby catalog target does not exist';
    END IF;

    IF current_value IS DISTINCT FROM ${expected_current} THEN
        RAISE EXCEPTION
            'lobby protocol translation expected %, found %',
            ${expected_current},
            current_value;
    END IF;

    IF ${desired} THEN
        SELECT count(*)
        INTO conflicting_targets
        FROM launcher.servers
        WHERE id <> 'lobby'
          AND allow_protocol_translation;

        IF conflicting_targets <> 0 THEN
            RAISE EXCEPTION
                'another target already allows protocol translation';
        END IF;
    END IF;

    UPDATE launcher.servers
    SET allow_protocol_translation = ${desired},
        updated_at = now()
    WHERE id = 'lobby';

    INSERT INTO launcher.audit_logs
        (action, target_type, target_id, before_data, after_data)
    VALUES
        (
            'catalog.server.protocol_translation.ops_set',
            'server',
            'lobby',
            jsonb_build_object(
                'allowsProtocolTranslation',
                current_value
            ),
            jsonb_build_object(
                'allowsProtocolTranslation',
                ${desired},
                'source',
                'root-ops-script'
            )
        );
END
\$operation\$;

COMMIT;

SELECT id,
       allow_protocol_translation,
       (
           SELECT count(*)
           FROM launcher.servers
           WHERE id <> 'lobby'
             AND allow_protocol_translation
       ) AS other_enabled_targets
FROM launcher.servers
WHERE id = 'lobby';"

docker exec "$postgres_container" sh -lc \
  'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d hechao_launcher -At -F "|" -c "$1"' \
  sh "$sql"

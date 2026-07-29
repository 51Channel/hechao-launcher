ALTER TABLE launcher.servers
    ADD COLUMN server_role text NOT NULL DEFAULT 'Player',
    ADD COLUMN monitoring_enabled boolean NOT NULL DEFAULT true;

ALTER TABLE launcher.servers
    ADD CONSTRAINT servers_role_check
        CHECK (server_role IN ('Player', 'Infrastructure'));

UPDATE launcher.servers
SET server_role = 'Infrastructure',
    is_visible = false,
    allow_protocol_translation = false,
    monitoring_enabled = true,
    revision = revision + 1,
    updated_at = now()
WHERE lower(id) = 'lobby'
   OR lower(velocity_target) = 'lobby';

ALTER TABLE launcher.servers
    ADD CONSTRAINT servers_infrastructure_isolation_check
        CHECK (
            server_role = 'Player'
            OR (NOT is_visible AND NOT allow_protocol_translation)
        );

ALTER TABLE launcher.servers
    ADD CONSTRAINT servers_lobby_is_always_infrastructure_check
        CHECK (
            (
                lower(id) <> 'lobby'
                AND lower(velocity_target) <> 'lobby'
            )
            OR (
                server_role = 'Infrastructure'
                AND NOT is_visible
                AND NOT allow_protocol_translation
            )
        );

CREATE INDEX servers_monitoring_target_idx
    ON launcher.servers (monitoring_enabled, velocity_target);

COMMENT ON COLUMN launcher.servers.server_role IS
    'Player targets may receive launcher grants; Infrastructure targets are internal-only.';

COMMENT ON COLUMN launcher.servers.monitoring_enabled IS
    'Controls heartbeat ingestion, runtime status, and operational alert evaluation independently from player visibility.';

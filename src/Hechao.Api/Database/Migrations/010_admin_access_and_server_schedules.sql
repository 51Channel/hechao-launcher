ALTER TABLE launcher.servers
    ADD COLUMN announcement text NOT NULL DEFAULT ''
        CHECK (length(announcement) <= 280),
    ADD COLUMN opens_at timestamp with time zone,
    ADD COLUMN closes_at timestamp with time zone,
    ADD CONSTRAINT servers_schedule_order
        CHECK (opens_at IS NULL OR closes_at IS NULL OR opens_at < closes_at);

ALTER TABLE launcher.server_access_overrides
    ADD COLUMN revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    ADD COLUMN updated_at timestamp with time zone NOT NULL DEFAULT now();

CREATE INDEX server_access_overrides_server_idx
    ON launcher.server_access_overrides (server_id, expires_at);

CREATE INDEX server_access_overrides_user_active_idx
    ON launcher.server_access_overrides (user_id, expires_at);

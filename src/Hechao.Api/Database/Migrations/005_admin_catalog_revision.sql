ALTER TABLE launcher.servers
    ADD COLUMN revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0);

CREATE INDEX audit_logs_target_idx
    ON launcher.audit_logs (target_type, target_id, created_at DESC);

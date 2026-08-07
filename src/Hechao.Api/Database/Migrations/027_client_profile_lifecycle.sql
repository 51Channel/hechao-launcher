ALTER TABLE launcher.client_profiles
    ADD COLUMN archived_at timestamp with time zone,
    ADD COLUMN archived_by uuid
        REFERENCES launcher.users(id) ON DELETE SET NULL,
    ADD COLUMN archive_reason text NOT NULL DEFAULT '';

ALTER TABLE launcher.client_profiles
    ADD CONSTRAINT client_profiles_archive_state_check CHECK (
        (
            archived_at IS NULL
            AND archived_by IS NULL
            AND archive_reason = ''
        )
        OR
        (
            archived_at IS NOT NULL
            AND NOT is_active
            AND length(btrim(archive_reason)) BETWEEN 4 AND 280
        )
    );

CREATE INDEX client_profiles_lifecycle_order_idx
    ON launcher.client_profiles (archived_at NULLS FIRST, is_active DESC, id);

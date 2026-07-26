CREATE TABLE launcher.diagnostic_uploads (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    profile_id text NOT NULL
        CHECK (profile_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    launcher_version text NOT NULL
        CHECK (length(launcher_version) BETWEEN 1 AND 40),
    expected_bytes bigint NOT NULL CHECK (expected_bytes > 0),
    expected_sha256 character(64) NOT NULL
        CHECK (expected_sha256 ~ '^[0-9a-f]{64}$'),
    actual_bytes bigint,
    actual_sha256 character(64),
    upload_token_sha256 character(64),
    upload_token_expires_at timestamp with time zone NOT NULL,
    status text NOT NULL
        CHECK (status IN ('pending', 'uploading', 'uploaded', 'failed', 'expired')),
    uploaded_at timestamp with time zone,
    expires_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CHECK (
        (status = 'uploaded' AND actual_bytes IS NOT NULL AND
         actual_sha256 IS NOT NULL AND uploaded_at IS NOT NULL AND expires_at IS NOT NULL)
        OR status <> 'uploaded'
    )
);

CREATE INDEX diagnostic_uploads_user_created_idx
    ON launcher.diagnostic_uploads (user_id, created_at DESC);

CREATE INDEX diagnostic_uploads_expiry_idx
    ON launcher.diagnostic_uploads (expires_at)
    WHERE status = 'uploaded';

CREATE INDEX diagnostic_uploads_ticket_expiry_idx
    ON launcher.diagnostic_uploads (upload_token_expires_at)
    WHERE status IN ('pending', 'uploading');

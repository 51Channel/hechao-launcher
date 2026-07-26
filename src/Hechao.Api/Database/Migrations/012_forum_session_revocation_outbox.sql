CREATE TABLE launcher.forum_session_revocation_outbox (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    requested_at timestamp with time zone NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    locked_until timestamp with time zone,
    completed_at timestamp with time zone,
    last_error text CHECK (last_error IS NULL OR length(last_error) <= 500),
    CHECK (completed_at IS NULL OR completed_at >= requested_at)
);

CREATE INDEX forum_session_revocation_outbox_due_idx
    ON launcher.forum_session_revocation_outbox
        (next_attempt_at, requested_at)
    WHERE completed_at IS NULL;

CREATE INDEX forum_session_revocation_outbox_user_idx
    ON launcher.forum_session_revocation_outbox
        (user_id, requested_at DESC);

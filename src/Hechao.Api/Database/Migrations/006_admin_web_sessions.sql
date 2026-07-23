CREATE TABLE launcher.admin_login_tickets (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    expires_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    source_ip inet,
    user_agent_hash bytea CHECK (
        user_agent_hash IS NULL OR octet_length(user_agent_hash) = 32
    ),
    CHECK (expires_at > created_at)
);

CREATE INDEX admin_login_tickets_active_idx
    ON launcher.admin_login_tickets (expires_at)
    WHERE consumed_at IS NULL;

CREATE TABLE launcher.admin_web_sessions (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    mfa_verified_at timestamp with time zone,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    last_seen_at timestamp with time zone NOT NULL DEFAULT now(),
    source_ip inet,
    user_agent_hash bytea CHECK (
        user_agent_hash IS NULL OR octet_length(user_agent_hash) = 32
    ),
    CHECK (expires_at > created_at)
);

CREATE INDEX admin_web_sessions_user_active_idx
    ON launcher.admin_web_sessions (user_id, expires_at DESC)
    WHERE revoked_at IS NULL;

CREATE INDEX admin_web_sessions_expiry_idx
    ON launcher.admin_web_sessions (expires_at);

CREATE TABLE launcher.admin_mfa_credentials (
    user_id uuid PRIMARY KEY REFERENCES launcher.users(id) ON DELETE CASCADE,
    secret_protected text NOT NULL CHECK (length(secret_protected) BETWEEN 40 AND 8192),
    recovery_code_hashes jsonb NOT NULL DEFAULT '[]'::jsonb
        CHECK (jsonb_typeof(recovery_code_hashes) = 'array'),
    last_accepted_time_window bigint,
    enabled_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE TABLE launcher.admin_mfa_enrollments (
    user_id uuid PRIMARY KEY REFERENCES launcher.users(id) ON DELETE CASCADE,
    secret_protected text NOT NULL CHECK (length(secret_protected) BETWEEN 40 AND 8192),
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CHECK (expires_at > created_at)
);

CREATE INDEX admin_mfa_enrollments_expiry_idx
    ON launcher.admin_mfa_enrollments (expires_at);

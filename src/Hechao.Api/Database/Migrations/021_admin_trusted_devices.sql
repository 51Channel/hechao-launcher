CREATE TABLE launcher.admin_trusted_devices (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    last_used_at timestamp with time zone NOT NULL DEFAULT now(),
    source_ip inet,
    last_source_ip inet,
    user_agent_hash bytea CHECK (
        user_agent_hash IS NULL OR octet_length(user_agent_hash) = 32
    ),
    CHECK (expires_at > created_at)
);

CREATE INDEX admin_trusted_devices_user_active_idx
    ON launcher.admin_trusted_devices (user_id, created_at DESC)
    WHERE revoked_at IS NULL;

CREATE INDEX admin_trusted_devices_expiry_idx
    ON launcher.admin_trusted_devices (expires_at);

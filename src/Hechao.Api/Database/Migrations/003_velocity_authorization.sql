CREATE TABLE launcher.velocity_launch_grants (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    minecraft_uuid uuid NOT NULL REFERENCES launcher.minecraft_identities(minecraft_uuid) ON DELETE CASCADE,
    requested_server_id text NOT NULL REFERENCES launcher.servers(id) ON DELETE CASCADE,
    source_ip inet,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    expires_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone,
    revoked_at timestamp with time zone,
    consumed_velocity_target text,
    proxy_instance text,
    CHECK (expires_at > created_at),
    CHECK (consumed_velocity_target IS NULL OR consumed_velocity_target ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    CHECK (proxy_instance IS NULL OR proxy_instance ~ '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$')
);

CREATE INDEX velocity_launch_grants_active_player_idx
    ON launcher.velocity_launch_grants (minecraft_uuid, expires_at DESC)
    WHERE consumed_at IS NULL AND revoked_at IS NULL;

CREATE INDEX velocity_launch_grants_expiry_idx
    ON launcher.velocity_launch_grants (expires_at);

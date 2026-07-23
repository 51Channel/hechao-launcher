CREATE TABLE launcher.luckperms_group_tier_mappings (
    primary_group text PRIMARY KEY CHECK (primary_group ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    access_tier text NOT NULL
        CHECK (access_tier IN ('Member', 'Participant', 'Collaborator', 'Administrator')),
    sort_weight integer NOT NULL CHECK (sort_weight BETWEEN 0 AND 10000),
    updated_at timestamp with time zone NOT NULL DEFAULT now()
);

INSERT INTO launcher.luckperms_group_tier_mappings
    (primary_group, access_tier, sort_weight)
VALUES
    ('default', 'Member', 0),
    ('vip', 'Participant', 50),
    ('admin', 'Collaborator', 90),
    ('owner', 'Administrator', 100);

CREATE TABLE launcher.luckperms_player_snapshots (
    minecraft_uuid uuid PRIMARY KEY,
    minecraft_name text NOT NULL CHECK (minecraft_name ~ '^[A-Za-z0-9_]{3,16}$'),
    primary_group text NOT NULL CHECK (primary_group ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    source_captured_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX luckperms_player_snapshots_group_idx
    ON launcher.luckperms_player_snapshots (primary_group);

ALTER TABLE launcher.minecraft_identities
    ADD COLUMN luckperms_primary_group text NOT NULL DEFAULT 'default'
        CHECK (luckperms_primary_group ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    ADD COLUMN luckperms_synced_at timestamp with time zone;

DROP INDEX launcher.minecraft_identities_name_ci_idx;
CREATE INDEX minecraft_identities_name_ci_idx
    ON launcher.minecraft_identities (lower(minecraft_name));

CREATE TABLE launcher.auth_sessions (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    access_token_hash bytea NOT NULL UNIQUE CHECK (octet_length(access_token_hash) = 32),
    refresh_token_hash bytea NOT NULL UNIQUE CHECK (octet_length(refresh_token_hash) = 32),
    access_expires_at timestamp with time zone NOT NULL,
    refresh_expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    last_seen_at timestamp with time zone NOT NULL DEFAULT now(),
    source_ip inet,
    user_agent_hash bytea CHECK (user_agent_hash IS NULL OR octet_length(user_agent_hash) = 32),
    CHECK (access_expires_at > created_at),
    CHECK (refresh_expires_at > access_expires_at)
);

CREATE INDEX auth_sessions_user_active_idx
    ON launcher.auth_sessions (user_id, refresh_expires_at DESC)
    WHERE revoked_at IS NULL;

CREATE INDEX auth_sessions_expiry_idx
    ON launcher.auth_sessions (refresh_expires_at);

CREATE TABLE launcher.client_profiles (
    id text PRIMARY KEY CHECK (id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    display_name text NOT NULL CHECK (length(display_name) BETWEEN 1 AND 80),
    version text NOT NULL CHECK (length(version) BETWEEN 1 AND 40),
    download_bytes bigint NOT NULL DEFAULT 0 CHECK (download_bytes >= 0),
    sha256 text NOT NULL DEFAULT '' CHECK (sha256 = '' OR sha256 ~ '^[0-9a-fA-F]{64}$'),
    published_at timestamp with time zone NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE TABLE launcher.servers (
    id text PRIMARY KEY CHECK (id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    display_name text NOT NULL CHECK (length(display_name) BETWEEN 1 AND 80),
    short_name text NOT NULL CHECK (length(short_name) BETWEEN 1 AND 12),
    icon_glyph text NOT NULL CHECK (length(icon_glyph) BETWEEN 1 AND 12),
    status text NOT NULL CHECK (status IN ('Online', 'Maintenance', 'Closed')),
    online_players integer NOT NULL DEFAULT 0 CHECK (online_players >= 0),
    max_players integer NOT NULL CHECK (max_players > 0),
    minecraft_version text NOT NULL CHECK (length(minecraft_version) BETWEEN 1 AND 40),
    loader text NOT NULL CHECK (loader IN ('Vanilla', 'Paper', 'NeoForge', 'Fabric', 'Forge')),
    minimum_tier text NOT NULL CHECK (minimum_tier IN ('Member', 'Participant', 'Collaborator', 'Administrator')),
    client_profile_id text NOT NULL REFERENCES launcher.client_profiles(id),
    velocity_target text NOT NULL CHECK (length(velocity_target) BETWEEN 1 AND 64),
    sort_order integer NOT NULL DEFAULT 0,
    is_visible boolean NOT NULL DEFAULT true,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CHECK (online_players <= max_players)
);

CREATE INDEX servers_visible_order_idx ON launcher.servers (is_visible, sort_order, id);

CREATE TABLE launcher.users (
    id uuid PRIMARY KEY,
    display_name text NOT NULL CHECK (length(display_name) BETWEEN 1 AND 80),
    access_tier text NOT NULL DEFAULT 'Member'
        CHECK (access_tier IN ('Member', 'Participant', 'Collaborator', 'Administrator')),
    is_disabled boolean NOT NULL DEFAULT false,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE TABLE launcher.minecraft_identities (
    minecraft_uuid uuid PRIMARY KEY,
    user_id uuid NOT NULL UNIQUE REFERENCES launcher.users(id) ON DELETE CASCADE,
    minecraft_name text NOT NULL CHECK (minecraft_name ~ '^[A-Za-z0-9_]{3,16}$'),
    verified_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX minecraft_identities_name_ci_idx
    ON launcher.minecraft_identities (lower(minecraft_name));

CREATE TABLE launcher.server_access_overrides (
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    server_id text NOT NULL REFERENCES launcher.servers(id) ON DELETE CASCADE,
    decision text NOT NULL CHECK (decision IN ('Allow', 'Deny')),
    reason text NOT NULL DEFAULT '',
    expires_at timestamp with time zone,
    created_by uuid REFERENCES launcher.users(id),
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, server_id)
);

CREATE TABLE launcher.audit_logs (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_user_id uuid REFERENCES launcher.users(id),
    action text NOT NULL CHECK (length(action) BETWEEN 1 AND 120),
    target_type text NOT NULL CHECK (length(target_type) BETWEEN 1 AND 80),
    target_id text NOT NULL CHECK (length(target_id) BETWEEN 1 AND 160),
    source_ip inet,
    before_data jsonb,
    after_data jsonb,
    created_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX audit_logs_created_at_idx ON launcher.audit_logs (created_at DESC);
CREATE INDEX audit_logs_actor_idx ON launcher.audit_logs (actor_user_id, created_at DESC);

INSERT INTO launcher.client_profiles
    (id, display_name, version, download_bytes, sha256, published_at)
VALUES
    ('base-1.21.11', '基础客户端', '1.0.4', 48234102, '', '2026-07-21T00:00:00Z'),
    ('activity-neoforge-1.21.11', '活动服模组包', '1.0.9', 132120576, '', '2026-07-21T00:00:00Z'),
    ('dollnight-1.21.11', '玩偶惊魂夜资源', '0.6.2', 82575360, '', '2026-07-21T00:00:00Z');

INSERT INTO launcher.servers
    (id, display_name, short_name, icon_glyph, status, online_players, max_players,
     minecraft_version, loader, minimum_tier, client_profile_id, velocity_target, sort_order)
VALUES
    ('lobby', '大厅', '厅', '⌂', 'Online', 42, 300, '1.21.11', 'Paper', 'Member', 'base-1.21.11', 'lobby', 10),
    ('survival2', '天域生存', '域', '山', 'Online', 18, 100, '1.21.11', 'Paper', 'Member', 'base-1.21.11', 'survival2', 20),
    ('activity', '海绵小镇躲猫猫', '海', '海', 'Online', 21, 30, '1.21.11', 'NeoForge', 'Participant', 'activity-neoforge-1.21.11', 'activity', 30),
    ('dollnight', '玩偶惊魂夜', '偶', '偶', 'Maintenance', 0, 30, '1.21.11', 'Paper', 'Participant', 'dollnight-1.21.11', 'survival2', 40);

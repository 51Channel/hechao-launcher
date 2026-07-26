ALTER TABLE launcher.client_profiles
    ADD COLUMN revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0);

CREATE TABLE launcher.client_profile_releases (
    manifest_sha256 text PRIMARY KEY
        CHECK (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    profile_id text NOT NULL
        REFERENCES launcher.client_profiles(id) ON DELETE RESTRICT,
    version text NOT NULL CHECK (length(version) BETWEEN 1 AND 40),
    download_bytes bigint NOT NULL CHECK (download_bytes >= 0),
    file_count integer NOT NULL CHECK (file_count > 0),
    minecraft_version text NOT NULL CHECK (length(minecraft_version) BETWEEN 1 AND 40),
    java_version text NOT NULL CHECK (length(java_version) BETWEEN 1 AND 40),
    loader text NOT NULL CHECK (length(loader) BETWEEN 1 AND 40),
    loader_version text NOT NULL CHECK (length(loader_version) BETWEEN 1 AND 80),
    published_at timestamp with time zone NOT NULL,
    is_paused boolean NOT NULL DEFAULT false,
    pause_reason text NOT NULL DEFAULT '' CHECK (length(pause_reason) <= 280),
    paused_at timestamp with time zone,
    paused_by uuid REFERENCES launcher.users(id) ON DELETE SET NULL,
    created_by uuid REFERENCES launcher.users(id) ON DELETE SET NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    UNIQUE (profile_id, version),
    UNIQUE (profile_id, manifest_sha256),
    CHECK (
        (is_paused AND paused_at IS NOT NULL AND pause_reason <> '')
        OR
        (NOT is_paused AND paused_at IS NULL AND pause_reason = '')
    )
);

CREATE INDEX client_profile_releases_profile_history_idx
    ON launcher.client_profile_releases
        (profile_id, published_at DESC, created_at DESC);

CREATE TABLE launcher.client_profile_channels (
    profile_id text NOT NULL
        REFERENCES launcher.client_profiles(id) ON DELETE CASCADE,
    channel text NOT NULL
        CHECK (channel IN ('test', 'gray', 'production')),
    release_sha256 text,
    rollout_percentage integer NOT NULL DEFAULT 0
        CHECK (rollout_percentage BETWEEN 0 AND 100),
    revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    updated_by uuid REFERENCES launcher.users(id) ON DELETE SET NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (profile_id, channel),
    FOREIGN KEY (profile_id, release_sha256)
        REFERENCES launcher.client_profile_releases(profile_id, manifest_sha256)
        ON DELETE RESTRICT,
    CHECK (
        (channel = 'production' AND rollout_percentage = 100)
        OR channel <> 'production'
    )
);

INSERT INTO launcher.client_profile_releases
    (
        manifest_sha256,
        profile_id,
        version,
        download_bytes,
        file_count,
        minecraft_version,
        java_version,
        loader,
        loader_version,
        published_at,
        created_at
    )
SELECT
    lower(profile.sha256),
    profile.id,
    profile.version,
    profile.download_bytes,
    1,
    'legacy',
    'legacy',
    'legacy',
    'legacy',
    profile.published_at,
    profile.published_at
FROM launcher.client_profiles profile
WHERE profile.sha256 <> ''
ON CONFLICT DO NOTHING;

INSERT INTO launcher.client_profile_channels
    (profile_id, channel, release_sha256, rollout_percentage, updated_at)
SELECT
    profile.id,
    channel.name,
    CASE
        WHEN channel.name = 'production' AND profile.sha256 <> ''
            THEN lower(profile.sha256)
        ELSE NULL
    END,
    CASE WHEN channel.name = 'production' THEN 100 ELSE 0 END,
    profile.updated_at
FROM launcher.client_profiles profile
CROSS JOIN (
    VALUES ('test'), ('gray'), ('production')
) AS channel(name);

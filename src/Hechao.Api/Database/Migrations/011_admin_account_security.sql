CREATE TABLE launcher.minecraft_identity_bans (
    minecraft_uuid uuid PRIMARY KEY,
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 4 AND 500),
    expires_at timestamp with time zone,
    created_by uuid NOT NULL REFERENCES launcher.users(id),
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    revoked_at timestamp with time zone,
    revoked_by uuid REFERENCES launcher.users(id),
    revoked_reason text CHECK (
        revoked_reason IS NULL OR length(btrim(revoked_reason)) BETWEEN 4 AND 500
    ),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    CHECK (expires_at IS NULL OR expires_at > created_at),
    CHECK (
        (revoked_at IS NULL AND revoked_by IS NULL AND revoked_reason IS NULL)
        OR
        (revoked_at IS NOT NULL AND revoked_by IS NOT NULL AND revoked_reason IS NOT NULL)
    )
);

CREATE INDEX minecraft_identity_bans_active_idx
    ON launcher.minecraft_identity_bans (expires_at)
    WHERE revoked_at IS NULL;

CREATE INDEX minecraft_identity_bans_created_by_idx
    ON launcher.minecraft_identity_bans (created_by, created_at DESC);

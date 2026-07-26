CREATE TABLE launcher.luckperms_tier_change_commands (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    minecraft_uuid uuid NOT NULL,
    expected_primary_group text NOT NULL
        CHECK (expected_primary_group ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    target_primary_group text NOT NULL
        CHECK (target_primary_group IN ('default', 'vip', 'admin', 'owner')),
    target_access_tier text NOT NULL
        CHECK (target_access_tier IN (
            'Member',
            'Participant',
            'Collaborator',
            'Administrator'
        )),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 4 AND 500),
    status text NOT NULL DEFAULT 'Pending'
        CHECK (status IN ('Pending', 'Claimed', 'Applied', 'Conflict', 'Failed')),
    requested_by uuid NOT NULL REFERENCES launcher.users(id),
    requested_at timestamp with time zone NOT NULL,
    claimed_by text,
    claimed_at timestamp with time zone,
    claim_expires_at timestamp with time zone,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    completed_at timestamp with time zone,
    observed_primary_group text
        CHECK (
            observed_primary_group IS NULL OR
            observed_primary_group ~ '^[a-z0-9][a-z0-9._-]{0,63}$'
        ),
    failure_code text
        CHECK (failure_code IS NULL OR length(failure_code) BETWEEN 1 AND 120),
    CHECK (
        (status = 'Pending' AND claimed_by IS NULL AND claimed_at IS NULL
            AND claim_expires_at IS NULL AND completed_at IS NULL)
        OR
        (status = 'Claimed' AND claimed_by IS NOT NULL AND claimed_at IS NOT NULL
            AND claim_expires_at IS NOT NULL AND completed_at IS NULL)
        OR
        (status IN ('Applied', 'Conflict', 'Failed') AND completed_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX luckperms_tier_change_commands_active_user_idx
    ON launcher.luckperms_tier_change_commands (user_id)
    WHERE status IN ('Pending', 'Claimed');

CREATE INDEX luckperms_tier_change_commands_claim_idx
    ON launcher.luckperms_tier_change_commands
        (status, claim_expires_at, requested_at);

CREATE INDEX luckperms_tier_change_commands_history_idx
    ON launcher.luckperms_tier_change_commands
        (user_id, requested_at DESC);

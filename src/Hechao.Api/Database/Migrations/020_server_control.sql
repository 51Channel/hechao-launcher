CREATE TABLE launcher.server_control_targets (
    server_id text PRIMARY KEY
        CHECK (server_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    agent_id text NOT NULL
        CHECK (agent_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    agent_version text NOT NULL CHECK (length(agent_version) BETWEEN 1 AND 40),
    conflict_group text
        CHECK (
            conflict_group IS NULL OR
            conflict_group ~ '^[a-z0-9][a-z0-9._-]{1,63}$'
        ),
    port integer NOT NULL CHECK (port BETWEEN 1 AND 65535),
    reported_online boolean NOT NULL,
    process_id integer CHECK (process_id > 0),
    settings jsonb,
    allowed_command_prefixes text[] NOT NULL,
    console_tail text NOT NULL DEFAULT '' CHECK (length(console_tail) <= 65536),
    console_captured_at timestamp with time zone,
    last_seen_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CHECK (
        (reported_online AND process_id IS NOT NULL) OR
        (NOT reported_online)
    )
);

CREATE INDEX server_control_targets_agent_idx
    ON launcher.server_control_targets (agent_id, last_seen_at DESC);

CREATE INDEX server_control_targets_conflict_idx
    ON launcher.server_control_targets (conflict_group, reported_online)
    WHERE conflict_group IS NOT NULL;

CREATE TABLE launcher.server_control_operations (
    id uuid PRIMARY KEY,
    target_server_id text NOT NULL,
    action text NOT NULL
        CHECK (action IN (
            'Start',
            'Stop',
            'Restart',
            'ConsoleCommand',
            'ApplySettings'
        )),
    status text NOT NULL DEFAULT 'Pending'
        CHECK (status IN (
            'Pending',
            'Running',
            'Succeeded',
            'Failed',
            'Cancelled'
        )),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 4 AND 500),
    requested_by uuid NOT NULL REFERENCES launcher.users(id),
    source_ip inet,
    requested_at timestamp with time zone NOT NULL,
    started_at timestamp with time zone,
    completed_at timestamp with time zone,
    result_code text
        CHECK (
            result_code IS NULL OR
            result_code ~ '^[A-Z][A-Z0-9_]{0,79}$'
        ),
    result_message text
        CHECK (
            result_message IS NULL OR
            length(result_message) BETWEEN 1 AND 2000
        ),
    automatically_stopping_server_ids text[] NOT NULL DEFAULT '{}',
    CHECK (
        (status = 'Pending' AND started_at IS NULL AND completed_at IS NULL)
        OR
        (status = 'Running' AND started_at IS NOT NULL AND completed_at IS NULL)
        OR
        (status IN ('Succeeded', 'Failed', 'Cancelled')
            AND completed_at IS NOT NULL)
    )
);

CREATE INDEX server_control_operations_target_history_idx
    ON launcher.server_control_operations
        (target_server_id, requested_at DESC);

CREATE INDEX server_control_operations_status_idx
    ON launcher.server_control_operations (status, requested_at);

CREATE TABLE launcher.server_control_commands (
    id uuid PRIMARY KEY,
    operation_id uuid NOT NULL
        REFERENCES launcher.server_control_operations(id) ON DELETE CASCADE,
    sequence integer NOT NULL CHECK (sequence >= 0),
    server_id text NOT NULL
        REFERENCES launcher.server_control_targets(server_id),
    agent_id text NOT NULL,
    kind text NOT NULL
        CHECK (kind IN ('Start', 'Stop', 'ConsoleCommand', 'ApplySettings')),
    payload jsonb NOT NULL,
    status text NOT NULL DEFAULT 'Pending'
        CHECK (status IN (
            'Pending',
            'Claimed',
            'Succeeded',
            'Failed',
            'Cancelled'
        )),
    claimed_by text,
    claimed_at timestamp with time zone,
    claim_expires_at timestamp with time zone,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    completed_at timestamp with time zone,
    result_code text
        CHECK (
            result_code IS NULL OR
            result_code ~ '^[A-Z][A-Z0-9_]{0,79}$'
        ),
    result_message text
        CHECK (
            result_message IS NULL OR
            length(result_message) BETWEEN 1 AND 2000
        ),
    UNIQUE (operation_id, sequence, server_id, kind),
    CHECK (
        (status = 'Pending' AND claimed_by IS NULL AND claimed_at IS NULL
            AND claim_expires_at IS NULL AND completed_at IS NULL)
        OR
        (status = 'Claimed' AND claimed_by IS NOT NULL AND claimed_at IS NOT NULL
            AND claim_expires_at IS NOT NULL AND completed_at IS NULL)
        OR
        (status IN ('Succeeded', 'Failed', 'Cancelled')
            AND completed_at IS NOT NULL)
    )
);

CREATE INDEX server_control_commands_claim_idx
    ON launcher.server_control_commands
        (agent_id, status, claim_expires_at, sequence);

CREATE INDEX server_control_commands_operation_idx
    ON launcher.server_control_commands
        (operation_id, sequence, status);

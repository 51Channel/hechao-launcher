ALTER TABLE launcher.server_control_operations
    DROP CONSTRAINT server_control_operations_action_check;

ALTER TABLE launcher.server_control_operations
    ADD CONSTRAINT server_control_operations_action_check
    CHECK (action IN (
        'Start',
        'Stop',
        'Restart',
        'ConsoleCommand',
        'ApplySettings',
        'DeployPackage',
        'DeleteServerFiles',
        'CreateDeploymentSlot'
    ));

ALTER TABLE launcher.server_control_commands
    DROP CONSTRAINT server_control_commands_kind_check;

ALTER TABLE launcher.server_control_commands
    ADD CONSTRAINT server_control_commands_kind_check
    CHECK (kind IN (
        'Start',
        'Stop',
        'ConsoleCommand',
        'ApplySettings',
        'DeployPackage',
        'DeleteServerFiles',
        'CreateDeploymentSlot'
    ));

CREATE TABLE launcher.deployment_slots (
    server_id text PRIMARY KEY
        REFERENCES launcher.server_control_targets(server_id) ON DELETE CASCADE,
    display_name text NOT NULL
        CHECK (length(btrim(display_name)) BETWEEN 2 AND 80),
    template_server_id text NOT NULL
        REFERENCES launcher.server_control_targets(server_id),
    status text NOT NULL DEFAULT 'Provisioning'
        CHECK (status IN ('Provisioning', 'Ready', 'Failed')),
    failure_code text
        CHECK (
            failure_code IS NULL OR
            failure_code ~ '^[A-Z][A-Z0-9_]{0,79}$'
        ),
    failure_message text
        CHECK (
            failure_message IS NULL OR
            length(failure_message) BETWEEN 1 AND 2000
        ),
    operation_id uuid NOT NULL UNIQUE
        REFERENCES launcher.server_control_operations(id),
    created_by uuid NOT NULL REFERENCES launcher.users(id),
    created_at timestamp with time zone NOT NULL,
    provisioned_at timestamp with time zone,
    updated_at timestamp with time zone NOT NULL,
    CHECK (
        (status = 'Provisioning' AND provisioned_at IS NULL
            AND failure_code IS NULL AND failure_message IS NULL)
        OR
        (status = 'Ready' AND provisioned_at IS NOT NULL
            AND failure_code IS NULL AND failure_message IS NULL)
        OR
        (status = 'Failed' AND provisioned_at IS NULL
            AND failure_code IS NOT NULL AND failure_message IS NOT NULL)
    )
);

CREATE INDEX deployment_slots_status_idx
    ON launcher.deployment_slots (status, updated_at DESC);

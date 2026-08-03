ALTER TABLE launcher.server_control_targets
    ADD COLUMN package_deployment_enabled boolean NOT NULL DEFAULT false;

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
        'DeployPackage'
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
        'DeployPackage'
    ));

CREATE UNIQUE INDEX package_imports_deployment_operation_idx
    ON launcher.package_imports (deployment_operation_id)
    WHERE deployment_operation_id IS NOT NULL;

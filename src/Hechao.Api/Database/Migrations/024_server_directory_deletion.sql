ALTER TABLE launcher.server_control_targets
    ADD COLUMN server_deletion_enabled boolean NOT NULL DEFAULT false,
    ADD COLUMN server_files_present boolean NOT NULL DEFAULT true,
    ADD COLUMN deletion_cleanup_pending boolean NOT NULL DEFAULT false;

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
        'DeleteServerFiles'
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
        'DeleteServerFiles'
    ));

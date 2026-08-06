ALTER TABLE launcher.server_control_targets
    ADD COLUMN host_total_memory_mib integer;

ALTER TABLE launcher.server_control_targets
    ADD CONSTRAINT server_control_targets_host_total_memory_check
    CHECK (
        host_total_memory_mib IS NULL OR
        host_total_memory_mib BETWEEN 1024 AND 1048576
    );

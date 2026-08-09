ALTER TABLE launcher.servers
    ADD COLUMN activity_package_import_id uuid
        REFERENCES launcher.package_imports(id) ON DELETE RESTRICT,
    ADD COLUMN activity_plan_status text,
    ADD CONSTRAINT servers_activity_plan_status_check CHECK (
        activity_plan_status IS NULL OR
        activity_plan_status IN ('Draft', 'Published', 'Archived')
    ),
    ADD CONSTRAINT servers_activity_plan_shape_check CHECK (
        activity_plan_status IS NULL OR (
            activity_package_import_id IS NOT NULL
            AND lower(velocity_target) = 'activity'
            AND status = 'Online'
            AND server_role = 'Player'
            AND monitoring_enabled
            AND NOT allow_protocol_translation
            AND opens_at IS NOT NULL
            AND closes_at IS NOT NULL
            AND is_visible = (activity_plan_status = 'Published')
        )
    );

ALTER TABLE launcher.server_control_targets
    ADD COLUMN deployed_package_import_id uuid
        REFERENCES launcher.package_imports(id) ON DELETE SET NULL,
    ADD COLUMN deployed_profile_id text,
    ADD COLUMN deployed_version text,
    ADD CONSTRAINT server_control_targets_deployment_identity_check CHECK (
        (
            deployed_package_import_id IS NULL
            AND deployed_profile_id IS NULL
            AND deployed_version IS NULL
        ) OR (
            deployed_package_import_id IS NOT NULL
            AND deployed_profile_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'
            AND length(deployed_version) BETWEEN 1 AND 40
        )
    );

CREATE TABLE launcher.activity_plan_deployments (
    operation_id uuid PRIMARY KEY
        REFERENCES launcher.server_control_operations(id) ON DELETE RESTRICT,
    activity_plan_id text NOT NULL
        REFERENCES launcher.servers(id) ON DELETE RESTRICT,
    package_import_id uuid NOT NULL
        REFERENCES launcher.package_imports(id) ON DELETE RESTRICT,
    requested_by uuid NOT NULL REFERENCES launcher.users(id),
    requested_at timestamp with time zone NOT NULL
);

CREATE INDEX activity_plan_deployments_plan_history_idx
    ON launcher.activity_plan_deployments
        (activity_plan_id, requested_at DESC);

CREATE INDEX servers_activity_plan_order_idx
    ON launcher.servers (opens_at, closes_at, id)
    WHERE activity_plan_status IS NOT NULL;

ALTER TABLE launcher.servers
    ADD CONSTRAINT servers_published_activity_schedule_exclusive
    EXCLUDE USING gist (
        tstzrange(opens_at, closes_at, '[)') WITH &&
    )
    WHERE (activity_plan_status = 'Published');

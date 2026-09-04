CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE launcher.unbound_activity_plans
    ADD COLUMN target_server_id text;

ALTER TABLE launcher.servers
    DROP CONSTRAINT servers_activity_plan_shape_check,
    DROP CONSTRAINT servers_published_activity_schedule_exclusive,
    ADD COLUMN activity_target_server_id text;

WITH valid_activity_targets AS (
    SELECT target.id
    FROM launcher.servers AS target
    JOIN launcher.server_control_targets AS control_target
      ON control_target.server_id = target.id
    LEFT JOIN launcher.deployment_slots AS deployment_slot
      ON deployment_slot.server_id = target.id
    WHERE target.activity_plan_status IS NULL
      AND target.server_role = 'Player'
      AND control_target.package_deployment_enabled
      AND control_target.agent_id = 'owl5'
      AND target.velocity_target = target.id
      AND (
          (
              target.id = 'activity'
              AND control_target.conflict_group = 'owl5-activity-slot'
              AND control_target.port = 25568
          ) OR (
              deployment_slot.status = 'Ready'
              AND deployment_slot.backend_port = control_target.port
              AND deployment_slot.velocity_target = target.id
              AND control_target.conflict_group IS NULL
              AND control_target.port BETWEEN 25600 AND 25611
          )
      )
)
UPDATE launcher.servers AS activity_plan
SET activity_target_server_id = COALESCE(
        (
            SELECT valid_target.id
            FROM launcher.package_imports AS package
            JOIN valid_activity_targets AS valid_target
              ON valid_target.id = package.plan ->> 'targetServerId'
            WHERE package.id = activity_plan.activity_package_import_id
            LIMIT 1
        ),
        (
            SELECT fallback.id
            FROM valid_activity_targets AS fallback
            WHERE fallback.id = 'activity'
            LIMIT 1
        )
    )
WHERE activity_plan.activity_plan_status IS NOT NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM launcher.servers
        WHERE activity_plan_status IS NOT NULL
          AND activity_target_server_id IS NULL
    ) THEN
        RAISE EXCEPTION
            'Cannot resolve a real target server for every existing activity plan';
    END IF;
END $$;

UPDATE launcher.servers AS activity_plan
SET velocity_target = target.velocity_target,
    monitoring_enabled = false
FROM launcher.servers AS target
WHERE activity_plan.activity_plan_status IS NOT NULL
  AND target.id = activity_plan.activity_target_server_id;

ALTER TABLE launcher.servers
    ADD CONSTRAINT servers_activity_target_server_fk
        FOREIGN KEY (activity_target_server_id)
        REFERENCES launcher.servers(id) ON DELETE RESTRICT,
    ADD CONSTRAINT servers_activity_plan_shape_check CHECK (
        (
            activity_plan_status IS NULL
            AND activity_target_server_id IS NULL
        ) OR (
            activity_plan_status IS NOT NULL
            AND activity_package_import_id IS NOT NULL
            AND activity_target_server_id IS NOT NULL
            AND activity_target_server_id <> id
            AND status = 'Online'
            AND server_role = 'Player'
            AND NOT monitoring_enabled
            AND NOT allow_protocol_translation
            AND opens_at IS NOT NULL
            AND closes_at IS NOT NULL
            AND is_visible = (activity_plan_status = 'Published')
        )
    ),
    ADD CONSTRAINT servers_published_activity_schedule_exclusive
        EXCLUDE USING gist (
            activity_target_server_id WITH =,
            tstzrange(opens_at, closes_at, '[)') WITH &&
        )
        WHERE (activity_plan_status = 'Published');

ALTER TABLE launcher.unbound_activity_plans
    ADD CONSTRAINT unbound_activity_plans_target_server_fk
        FOREIGN KEY (target_server_id)
        REFERENCES launcher.servers(id) ON DELETE RESTRICT;

CREATE INDEX servers_activity_target_idx
    ON launcher.servers (activity_target_server_id, activity_plan_status)
    WHERE activity_target_server_id IS NOT NULL;

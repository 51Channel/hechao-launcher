ALTER TABLE launcher.deployment_slots
    ADD COLUMN slot_kind text NOT NULL DEFAULT 'Activity'
        CHECK (slot_kind IN ('Activity', 'Survival', 'Pvp', 'Minigame')),
    ADD COLUMN backend_port integer,
    ADD COLUMN velocity_target text;

UPDATE launcher.deployment_slots AS slot
SET backend_port = target.port,
    velocity_target = CASE
        WHEN target.port = 25568 THEN 'activity'
        ELSE slot.server_id
    END
FROM launcher.server_control_targets AS target
WHERE target.server_id = slot.server_id;

ALTER TABLE launcher.deployment_slots
    ALTER COLUMN backend_port SET NOT NULL,
    ALTER COLUMN velocity_target SET NOT NULL,
    ADD CONSTRAINT deployment_slots_backend_port_check
        CHECK (backend_port = 25568 OR backend_port BETWEEN 25600 AND 25611),
    ADD CONSTRAINT deployment_slots_velocity_target_check
        CHECK (velocity_target ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    ADD CONSTRAINT deployment_slots_routing_mode_check
        CHECK (
            (backend_port = 25568 AND velocity_target = 'activity')
            OR
            (backend_port BETWEEN 25600 AND 25611
                AND velocity_target = server_id)
        );

CREATE UNIQUE INDEX deployment_slots_independent_port_idx
    ON launcher.deployment_slots (backend_port)
    WHERE status IN ('Provisioning', 'Ready')
      AND backend_port BETWEEN 25600 AND 25611;

CREATE UNIQUE INDEX deployment_slots_velocity_target_idx
    ON launcher.deployment_slots (velocity_target)
    WHERE status IN ('Provisioning', 'Ready')
      AND velocity_target <> 'activity';

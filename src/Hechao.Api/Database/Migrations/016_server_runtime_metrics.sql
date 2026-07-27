ALTER TABLE launcher.velocity_target_heartbeats
    ADD COLUMN process_working_set_bytes bigint
        CHECK (
            process_working_set_bytes IS NULL
            OR process_working_set_bytes BETWEEN 0 AND 17592186044416
        ),
    ADD COLUMN process_private_bytes bigint
        CHECK (
            process_private_bytes IS NULL
            OR process_private_bytes BETWEEN 0 AND 17592186044416
        ),
    ADD COLUMN process_cpu_percent double precision
        CHECK (
            process_cpu_percent IS NULL
            OR process_cpu_percent BETWEEN 0 AND 100
        ),
    ADD COLUMN process_started_at timestamp with time zone,
    ADD COLUMN disk_free_bytes bigint
        CHECK (
            disk_free_bytes IS NULL
            OR disk_free_bytes BETWEEN 0 AND 1125899906842624
        ),
    ADD COLUMN disk_total_bytes bigint
        CHECK (
            disk_total_bytes IS NULL
            OR disk_total_bytes BETWEEN 0 AND 1125899906842624
        ),
    ADD COLUMN tps_1m double precision
        CHECK (tps_1m IS NULL OR tps_1m BETWEEN 0 AND 20.1),
    ADD COLUMN tps_5m double precision
        CHECK (tps_5m IS NULL OR tps_5m BETWEEN 0 AND 20.1),
    ADD COLUMN tps_15m double precision
        CHECK (tps_15m IS NULL OR tps_15m BETWEEN 0 AND 20.1),
    ADD COLUMN mspt_average double precision
        CHECK (
            mspt_average IS NULL
            OR mspt_average BETWEEN 0 AND 60000
        ),
    ADD COLUMN gc_collection_time_ms bigint
        CHECK (
            gc_collection_time_ms IS NULL
            OR gc_collection_time_ms BETWEEN 0 AND 31536000000
        ),
    ADD COLUMN metrics_captured_at timestamp with time zone,
    ADD COLUMN probe_issues text[] NOT NULL DEFAULT '{}',
    ADD CONSTRAINT velocity_target_heartbeats_process_metrics_check CHECK (
        (
            process_working_set_bytes IS NULL
            AND process_private_bytes IS NULL
            AND process_cpu_percent IS NULL
            AND process_started_at IS NULL
        )
        OR
        (
            process_working_set_bytes IS NOT NULL
            AND process_private_bytes IS NOT NULL
            AND process_cpu_percent IS NOT NULL
            AND process_started_at IS NOT NULL
        )
    ),
    ADD CONSTRAINT velocity_target_heartbeats_disk_metrics_check CHECK (
        (
            disk_free_bytes IS NULL
            AND disk_total_bytes IS NULL
        )
        OR
        (
            disk_free_bytes IS NOT NULL
            AND disk_total_bytes IS NOT NULL
            AND disk_free_bytes <= disk_total_bytes
        )
    ),
    ADD CONSTRAINT velocity_target_heartbeats_tick_metrics_check CHECK (
        (
            tps_1m IS NULL
            AND tps_5m IS NULL
            AND tps_15m IS NULL
            AND mspt_average IS NULL
            AND metrics_captured_at IS NULL
        )
        OR
        (
            tps_1m IS NOT NULL
            AND tps_5m IS NOT NULL
            AND tps_15m IS NOT NULL
            AND mspt_average IS NOT NULL
            AND metrics_captured_at IS NOT NULL
        )
    );

CREATE TABLE launcher.server_runtime_samples (
    velocity_target text NOT NULL
        CHECK (velocity_target ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    collector_instance text NOT NULL
        CHECK (collector_instance ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'),
    is_online boolean NOT NULL,
    online_players integer NOT NULL CHECK (online_players >= 0),
    max_players integer NOT NULL CHECK (max_players BETWEEN 0 AND 10000),
    process_working_set_bytes bigint,
    process_private_bytes bigint,
    process_cpu_percent double precision,
    process_started_at timestamp with time zone,
    disk_free_bytes bigint,
    disk_total_bytes bigint,
    tps_1m double precision,
    tps_5m double precision,
    tps_15m double precision,
    mspt_average double precision,
    gc_collection_time_ms bigint,
    metrics_captured_at timestamp with time zone,
    probe_issues text[] NOT NULL DEFAULT '{}',
    captured_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (velocity_target, captured_at),
    CHECK (online_players <= max_players),
    CHECK (is_online OR online_players = 0),
    CHECK (process_working_set_bytes IS NULL OR process_working_set_bytes >= 0),
    CHECK (process_private_bytes IS NULL OR process_private_bytes >= 0),
    CHECK (
        process_cpu_percent IS NULL
        OR process_cpu_percent BETWEEN 0 AND 100
    ),
    CHECK (disk_free_bytes IS NULL OR disk_free_bytes >= 0),
    CHECK (disk_total_bytes IS NULL OR disk_total_bytes >= 0),
    CHECK (
        disk_free_bytes IS NULL
        OR disk_total_bytes IS NULL
        OR disk_free_bytes <= disk_total_bytes
    ),
    CHECK (tps_1m IS NULL OR tps_1m BETWEEN 0 AND 20.1),
    CHECK (tps_5m IS NULL OR tps_5m BETWEEN 0 AND 20.1),
    CHECK (tps_15m IS NULL OR tps_15m BETWEEN 0 AND 20.1),
    CHECK (
        mspt_average IS NULL
        OR mspt_average BETWEEN 0 AND 60000
    ),
    CHECK (
        gc_collection_time_ms IS NULL
        OR gc_collection_time_ms BETWEEN 0 AND 31536000000
    )
);

CREATE INDEX server_runtime_samples_received_idx
    ON launcher.server_runtime_samples (received_at DESC);

CREATE INDEX server_runtime_samples_target_idx
    ON launcher.server_runtime_samples (velocity_target, captured_at DESC);

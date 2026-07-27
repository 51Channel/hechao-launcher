CREATE TABLE launcher.api_request_minute_metrics (
    bucket_start timestamp with time zone NOT NULL,
    category text NOT NULL
        CHECK (category IN ('All', 'Login', 'ObjectDownload')),
    request_count bigint NOT NULL CHECK (request_count >= 0),
    client_error_count bigint NOT NULL CHECK (client_error_count >= 0),
    server_error_count bigint NOT NULL CHECK (server_error_count >= 0),
    total_duration_ms bigint NOT NULL CHECK (total_duration_ms >= 0),
    maximum_duration_ms integer NOT NULL CHECK (maximum_duration_ms >= 0),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (bucket_start, category),
    CHECK (client_error_count + server_error_count <= request_count)
);

CREATE INDEX api_request_minute_metrics_updated_idx
    ON launcher.api_request_minute_metrics (updated_at DESC);

CREATE TABLE launcher.operational_alerts (
    fingerprint text PRIMARY KEY
        CHECK (
            fingerprint ~ '^[a-z0-9][a-z0-9:._-]{2,159}$'
        ),
    code text NOT NULL
        CHECK (code ~ '^[A-Za-z][A-Za-z0-9.]{2,79}$'),
    source text NOT NULL
        CHECK (source IN (
            'Api',
            'Authentication',
            'Distribution',
            'Server',
            'Certificate',
            'Infrastructure'
        )),
    severity text NOT NULL
        CHECK (severity IN ('Info', 'Warning', 'Critical')),
    status text NOT NULL
        CHECK (status IN ('Active', 'Resolved')),
    producer text NOT NULL
        CHECK (producer IN ('ApiEvaluator', 'PlatformMonitor')),
    title text NOT NULL CHECK (length(btrim(title)) BETWEEN 2 AND 120),
    summary text NOT NULL CHECK (length(btrim(summary)) BETWEEN 2 AND 500),
    opened_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL,
    last_transition_at timestamp with time zone NOT NULL,
    resolved_at timestamp with time zone,
    observation_count bigint NOT NULL DEFAULT 1
        CHECK (observation_count > 0),
    acknowledged_at timestamp with time zone,
    acknowledged_by uuid REFERENCES launcher.users(id),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    CHECK (last_seen_at >= opened_at),
    CHECK (last_transition_at >= opened_at),
    CHECK (
        (status = 'Active' AND resolved_at IS NULL)
        OR
        (status = 'Resolved' AND resolved_at IS NOT NULL)
    ),
    CHECK (
        (acknowledged_at IS NULL AND acknowledged_by IS NULL)
        OR
        (acknowledged_at IS NOT NULL AND acknowledged_by IS NOT NULL)
    )
);

CREATE INDEX operational_alerts_active_idx
    ON launcher.operational_alerts
        (severity, last_seen_at DESC)
    WHERE status = 'Active';

CREATE INDEX operational_alerts_transition_idx
    ON launcher.operational_alerts (last_transition_at DESC);

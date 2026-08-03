CREATE TABLE launcher.package_publisher_agents (
    agent_id text PRIMARY KEY
        CHECK (agent_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    agent_version text NOT NULL CHECK (length(agent_version) BETWEEN 1 AND 40),
    captured_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL
);

CREATE TABLE launcher.package_imports (
    id uuid PRIMARY KEY,
    file_name text NOT NULL CHECK (length(file_name) BETWEEN 5 AND 180),
    expected_upload_bytes bigint NOT NULL CHECK (expected_upload_bytes >= 1024),
    uploaded_bytes bigint NOT NULL DEFAULT 0
        CHECK (uploaded_bytes >= 0 AND uploaded_bytes <= expected_upload_bytes),
    source_sha256 text CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
    status text NOT NULL DEFAULT 'Uploading'
        CHECK (status IN (
            'Uploading', 'Uploaded', 'Analyzing', 'AwaitingReview',
            'QueuedForPublishing', 'PublishingClient',
            'QueuedForDeployment', 'DeployingServer', 'Finalizing',
            'Completed', 'Failed', 'Cancelled'
        )),
    analysis jsonb,
    plan jsonb,
    manifest_sha256 text CHECK (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    deployment_operation_id uuid
        REFERENCES launcher.server_control_operations(id),
    analysis_started_at timestamp with time zone,
    analysis_attempt_count integer NOT NULL DEFAULT 0
        CHECK (analysis_attempt_count BETWEEN 0 AND 5),
    publisher_claimed_by text,
    publisher_claimed_at timestamp with time zone,
    publisher_lease_expires_at timestamp with time zone,
    publisher_attempt_count integer NOT NULL DEFAULT 0
        CHECK (publisher_attempt_count BETWEEN 0 AND 5),
    publisher_uploaded_objects integer NOT NULL DEFAULT 0
        CHECK (publisher_uploaded_objects >= 0),
    publisher_existing_objects integer NOT NULL DEFAULT 0
        CHECK (publisher_existing_objects >= 0),
    publisher_uploaded_bytes bigint NOT NULL DEFAULT 0
        CHECK (publisher_uploaded_bytes >= 0),
    error_code text CHECK (
        error_code IS NULL OR error_code ~ '^[A-Z][A-Z0-9_]{0,79}$'
    ),
    error_message text CHECK (
        error_message IS NULL OR length(error_message) BETWEEN 1 AND 2000
    ),
    created_by uuid NOT NULL REFERENCES launcher.users(id),
    source_ip inet,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone,
    revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    CHECK (
        (status IN ('Uploaded', 'Analyzing', 'AwaitingReview',
                    'QueuedForPublishing', 'PublishingClient',
                    'QueuedForDeployment', 'DeployingServer', 'Finalizing',
                    'Completed')
         AND uploaded_bytes = expected_upload_bytes
         AND source_sha256 IS NOT NULL)
        OR status IN ('Uploading', 'Failed', 'Cancelled')
    ),
    CHECK (
        (status = 'PublishingClient'
         AND publisher_claimed_by IS NOT NULL
         AND publisher_claimed_at IS NOT NULL
         AND publisher_lease_expires_at IS NOT NULL)
        OR status <> 'PublishingClient'
    ),
    CHECK (
        (status IN ('Completed', 'Failed', 'Cancelled') AND completed_at IS NOT NULL)
        OR (status NOT IN ('Completed', 'Failed', 'Cancelled') AND completed_at IS NULL)
    )
);

CREATE INDEX package_imports_status_idx
    ON launcher.package_imports (status, updated_at);

CREATE INDEX package_imports_created_idx
    ON launcher.package_imports (created_at DESC);

CREATE TABLE launcher.package_import_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    import_id uuid NOT NULL
        REFERENCES launcher.package_imports(id) ON DELETE CASCADE,
    status text NOT NULL,
    code text NOT NULL CHECK (code ~ '^[A-Z][A-Z0-9_]{0,79}$'),
    message text NOT NULL CHECK (length(message) BETWEEN 1 AND 1000),
    created_at timestamp with time zone NOT NULL
);

CREATE INDEX package_import_events_history_idx
    ON launcher.package_import_events (import_id, created_at, id);

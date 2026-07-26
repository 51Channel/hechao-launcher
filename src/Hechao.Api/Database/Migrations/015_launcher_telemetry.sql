CREATE TABLE launcher.client_telemetry_events (
    user_id uuid NOT NULL
        REFERENCES launcher.users(id) ON DELETE CASCADE,
    event_id uuid NOT NULL,
    event_type text NOT NULL
        CHECK (event_type IN (
            'LauncherStarted',
            'Install',
            'Repair',
            'Rollback',
            'Launch',
            'GameExit'
        )),
    outcome text NOT NULL
        CHECK (outcome IN ('Success', 'Failure', 'Canceled')),
    failure_code text NOT NULL
        CHECK (failure_code IN (
            'None',
            'UserCanceled',
            'AuthenticationRequired',
            'ProfileUnavailable',
            'ApiUnavailable',
            'SignatureInvalid',
            'IntegrityFailed',
            'InsufficientDiskSpace',
            'InstallBusy',
            'RuntimePreparationFailed',
            'NetworkUnavailable',
            'IoFailure',
            'RollbackUnavailable',
            'MinecraftIdentityRequired',
            'MicrosoftReauthenticationRequired',
            'MicrosoftNotConfigured',
            'MicrosoftCanceled',
            'MicrosoftAccountMismatch',
            'MicrosoftSignInFailed',
            'MinecraftOwnership',
            'MinecraftSessionExpired',
            'LaunchAuthorizationFailed',
            'GameAlreadyRunning',
            'InvalidProfile',
            'InvalidJavaSelection',
            'ProcessCreationFailed',
            'GameExitedNonZero',
            'Unexpected'
        )),
    launcher_version text NOT NULL
        CHECK (length(launcher_version) BETWEEN 1 AND 40),
    profile_id text
        CHECK (
            profile_id IS NULL
            OR profile_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'
        ),
    profile_version text
        CHECK (
            profile_version IS NULL
            OR length(profile_version) BETWEEN 1 AND 40
        ),
    duration_ms integer
        CHECK (duration_ms IS NULL OR duration_ms BETWEEN 0 AND 86400000),
    bytes bigint
        CHECK (bytes IS NULL OR bytes BETWEEN 0 AND 1099511627776),
    occurred_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, event_id),
    CHECK (
        (outcome = 'Success' AND failure_code = 'None')
        OR
        (outcome <> 'Success' AND failure_code <> 'None')
    ),
    CHECK (
        (profile_id IS NULL AND profile_version IS NULL)
        OR
        (profile_id IS NOT NULL AND profile_version IS NOT NULL)
    )
);

CREATE INDEX client_telemetry_received_idx
    ON launcher.client_telemetry_events (received_at DESC);

CREATE INDEX client_telemetry_operation_idx
    ON launcher.client_telemetry_events
        (event_type, occurred_at DESC, outcome);

CREATE INDEX client_telemetry_launcher_version_idx
    ON launcher.client_telemetry_events
        (launcher_version, occurred_at DESC);

CREATE TABLE launcher.velocity_target_heartbeats (
    velocity_target text PRIMARY KEY
        CHECK (velocity_target ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    collector_instance text NOT NULL
        CHECK (collector_instance ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'),
    is_online boolean NOT NULL,
    online_players integer NOT NULL CHECK (online_players >= 0),
    max_players integer NOT NULL CHECK (max_players BETWEEN 0 AND 10000),
    software_version text CHECK (
        software_version IS NULL OR length(software_version) BETWEEN 1 AND 120),
    protocol_version integer CHECK (
        protocol_version IS NULL OR protocol_version BETWEEN 0 AND 100000),
    captured_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL DEFAULT now(),
    CHECK (online_players <= max_players),
    CHECK (is_online OR online_players = 0),
    CHECK (NOT is_online OR max_players > 0)
);

CREATE INDEX velocity_target_heartbeats_received_at_idx
    ON launcher.velocity_target_heartbeats (received_at DESC);

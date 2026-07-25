CREATE UNIQUE INDEX users_display_name_unique_idx
    ON launcher.users (display_name);

CREATE TABLE launcher.external_identities (
    provider text NOT NULL
        CHECK (provider ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    subject text NOT NULL
        CHECK (length(subject) BETWEEN 1 AND 160),
    user_id uuid NOT NULL REFERENCES launcher.users(id) ON DELETE CASCADE,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (provider, subject),
    UNIQUE (provider, user_id)
);

CREATE INDEX external_identities_user_idx
    ON launcher.external_identities (user_id);

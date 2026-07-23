ALTER TABLE launcher.users
    ADD COLUMN username text,
    ADD COLUMN email text,
    ADD COLUMN password_hash text;

UPDATE launcher.users
SET username = 'legacy_' || replace(id::text, '-', '');

ALTER TABLE launcher.users
    ALTER COLUMN username SET NOT NULL,
    ADD CONSTRAINT users_username_shape
        CHECK (username ~ '^[a-z0-9_]{3,40}$'),
    ADD CONSTRAINT users_email_shape
        CHECK (
            email IS NULL OR
            (length(email) BETWEEN 5 AND 254 AND email = lower(email) AND position('@' IN email) > 1)
        ),
    ADD CONSTRAINT users_password_hash_length
        CHECK (password_hash IS NULL OR length(password_hash) BETWEEN 20 AND 1024);

CREATE UNIQUE INDEX users_username_ci_idx
    ON launcher.users (lower(username));

CREATE UNIQUE INDEX users_email_ci_idx
    ON launcher.users (lower(email))
    WHERE email IS NOT NULL;

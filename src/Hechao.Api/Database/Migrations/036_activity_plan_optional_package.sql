CREATE TABLE launcher.unbound_activity_plans (
    id text PRIMARY KEY CHECK (id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    display_name text NOT NULL CHECK (length(display_name) BETWEEN 1 AND 80),
    short_name text NOT NULL CHECK (length(short_name) BETWEEN 1 AND 12),
    announcement text NOT NULL DEFAULT '' CHECK (length(announcement) <= 280),
    opens_at timestamp with time zone NOT NULL,
    closes_at timestamp with time zone NOT NULL,
    max_players integer NOT NULL CHECK (max_players BETWEEN 1 AND 1000),
    minimum_tier text NOT NULL
        CHECK (minimum_tier IN ('Member', 'Participant', 'Collaborator')),
    activity_plan_status text NOT NULL
        CHECK (activity_plan_status IN ('Draft', 'Archived')),
    revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CHECK (opens_at < closes_at)
);

CREATE INDEX unbound_activity_plans_order_idx
    ON launcher.unbound_activity_plans (opens_at, closes_at, id);

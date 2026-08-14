CREATE TABLE launcher.economy_accounts (
    player_uuid uuid PRIMARY KEY,
    available_balance numeric(19, 2) NOT NULL DEFAULT 0
        CHECK (available_balance >= 0),
    frozen_balance numeric(19, 2) NOT NULL DEFAULT 0
        CHECK (frozen_balance >= 0),
    updated_at timestamp with time zone NOT NULL
);

CREATE TABLE launcher.economy_operations (
    operation_id uuid PRIMARY KEY,
    server_id text NOT NULL
        CHECK (server_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    idempotency_key text NOT NULL
        CHECK (idempotency_key ~ '^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$'),
    request_fingerprint text NOT NULL
        CHECK (request_fingerprint ~ '^[0-9a-f]{64}$'),
    operation_kind text NOT NULL
        CHECK (operation_kind IN ('Transfer', 'Sale')),
    status text NOT NULL
        CHECK (status IN ('Pending', 'Applied', 'Rejected')),
    response_json jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    UNIQUE (server_id, idempotency_key)
);

CREATE TABLE launcher.economy_ledger_entries (
    operation_id uuid NOT NULL
        REFERENCES launcher.economy_operations(operation_id) ON DELETE RESTRICT,
    account_key text NOT NULL,
    amount numeric(19, 2) NOT NULL CHECK (amount <> 0),
    player_uuid uuid,
    PRIMARY KEY (operation_id, account_key),
    CHECK (
        (account_key LIKE 'player:%' AND player_uuid IS NOT NULL)
        OR (account_key LIKE 'system:%' AND player_uuid IS NULL)
    )
);

CREATE TABLE launcher.economy_products (
    item_id text PRIMARY KEY
        CHECK (item_id ~ '^minecraft:[a-z0-9_./-]{1,96}$'),
    unit_price numeric(19, 2) NOT NULL CHECK (unit_price > 0),
    personal_daily_limit integer NOT NULL CHECK (personal_daily_limit > 0),
    server_daily_limit integer NOT NULL CHECK (server_daily_limit > 0),
    enabled boolean NOT NULL DEFAULT true,
    updated_by_uuid uuid NOT NULL,
    updated_by_name text NOT NULL CHECK (length(btrim(updated_by_name)) BETWEEN 1 AND 64),
    updated_at timestamp with time zone NOT NULL
);

CREATE TABLE launcher.economy_product_audit (
    id bigserial PRIMARY KEY,
    item_id text NOT NULL,
    action text NOT NULL CHECK (action IN ('Upsert', 'Disable')),
    actor_uuid uuid NOT NULL,
    actor_name text NOT NULL,
    before_json jsonb,
    after_json jsonb,
    created_at timestamp with time zone NOT NULL
);

CREATE TABLE launcher.economy_sale_quotes (
    quote_id uuid PRIMARY KEY,
    server_id text NOT NULL,
    player_uuid uuid NOT NULL,
    item_id text NOT NULL REFERENCES launcher.economy_products(item_id),
    quantity integer NOT NULL CHECK (quantity > 0),
    unit_price numeric(19, 2) NOT NULL CHECK (unit_price > 0),
    total_amount numeric(19, 2) NOT NULL CHECK (total_amount > 0),
    status text NOT NULL DEFAULT 'Open'
        CHECK (status IN ('Open', 'Committed', 'Expired')),
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL CHECK (expires_at > created_at),
    committed_operation_id uuid
        REFERENCES launcher.economy_operations(operation_id) ON DELETE RESTRICT,
    CHECK (
        (status = 'Committed' AND committed_operation_id IS NOT NULL)
        OR (status <> 'Committed' AND committed_operation_id IS NULL)
    )
);

CREATE TABLE launcher.economy_sale_usage (
    usage_date date NOT NULL,
    server_id text NOT NULL,
    player_uuid uuid NOT NULL,
    item_id text NOT NULL REFERENCES launcher.economy_products(item_id),
    quantity integer NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (usage_date, server_id, player_uuid, item_id)
);

CREATE INDEX economy_operations_created_idx
    ON launcher.economy_operations (created_at DESC);

CREATE INDEX economy_sale_quotes_expiry_idx
    ON launcher.economy_sale_quotes (status, expires_at);

CREATE INDEX economy_sale_usage_server_idx
    ON launcher.economy_sale_usage (usage_date, server_id, item_id);

CREATE OR REPLACE FUNCTION launcher.enforce_economy_operation_balanced()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    total numeric(19, 2);
BEGIN
    SELECT COALESCE(sum(amount), 0)
    INTO total
    FROM launcher.economy_ledger_entries
    WHERE operation_id = NEW.operation_id;

    IF total <> 0 THEN
        RAISE EXCEPTION 'economy operation % is not balanced', NEW.operation_id;
    END IF;
    RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER economy_operation_balanced
AFTER INSERT OR UPDATE OR DELETE ON launcher.economy_ledger_entries
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION launcher.enforce_economy_operation_balanced();

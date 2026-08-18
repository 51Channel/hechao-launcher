ALTER TABLE launcher.economy_operations
    DROP CONSTRAINT economy_operations_operation_kind_check;

ALTER TABLE launcher.economy_operations
    ADD CONSTRAINT economy_operations_operation_kind_check
    CHECK (operation_kind IN (
        'Transfer',
        'Sale',
        'MarketList',
        'MarketBuy',
        'MarketCancel',
        'MarketClaim'
    ));

CREATE TABLE launcher.economy_market_listings (
    listing_id uuid PRIMARY KEY,
    server_id text NOT NULL
        CHECK (server_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    seller_uuid uuid NOT NULL,
    seller_name text NOT NULL
        CHECK (length(btrim(seller_name)) BETWEEN 1 AND 64),
    item_id text NOT NULL
        CHECK (item_id ~ '^[a-z0-9_.-]{1,64}:[a-z0-9_./-]{1,96}$'),
    quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 2304),
    total_price numeric(19, 2) NOT NULL CHECK (total_price >= 1),
    listing_fee numeric(19, 2) NOT NULL CHECK (listing_fee >= 0),
    status text NOT NULL DEFAULT 'Active'
        CHECK (status IN ('Active', 'Sold', 'Cancelled', 'Expired', 'Frozen')),
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL CHECK (expires_at > created_at),
    sold_at timestamp with time zone,
    buyer_uuid uuid,
    creation_operation_id uuid NOT NULL
        REFERENCES launcher.economy_operations(operation_id) ON DELETE RESTRICT,
    completion_operation_id uuid
        REFERENCES launcher.economy_operations(operation_id) ON DELETE RESTRICT,
    CHECK (
        (status = 'Sold' AND sold_at IS NOT NULL AND buyer_uuid IS NOT NULL
         AND completion_operation_id IS NOT NULL)
        OR (status <> 'Sold' AND sold_at IS NULL AND buyer_uuid IS NULL)
    )
);

CREATE TABLE launcher.economy_market_deliveries (
    delivery_id uuid PRIMARY KEY,
    player_uuid uuid NOT NULL,
    source_listing_id uuid NOT NULL
        REFERENCES launcher.economy_market_listings(listing_id) ON DELETE RESTRICT,
    server_id text NOT NULL
        CHECK (server_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    item_id text NOT NULL
        CHECK (item_id ~ '^[a-z0-9_.-]{1,64}:[a-z0-9_./-]{1,96}$'),
    quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 2304),
    reason text NOT NULL CHECK (reason IN ('Purchase', 'Cancelled', 'Expired')),
    status text NOT NULL DEFAULT 'Pending'
        CHECK (status IN ('Pending', 'Claimed')),
    created_at timestamp with time zone NOT NULL,
    claimed_at timestamp with time zone,
    claim_operation_id uuid
        REFERENCES launcher.economy_operations(operation_id) ON DELETE RESTRICT,
    UNIQUE (source_listing_id, player_uuid, reason),
    CHECK (
        (status = 'Claimed' AND claimed_at IS NOT NULL AND claim_operation_id IS NOT NULL)
        OR (status = 'Pending' AND claimed_at IS NULL AND claim_operation_id IS NULL)
    )
);

CREATE INDEX economy_market_active_idx
    ON launcher.economy_market_listings (server_id, created_at DESC)
    WHERE status = 'Active';

CREATE INDEX economy_market_seller_idx
    ON launcher.economy_market_listings (server_id, seller_uuid, created_at DESC);

CREATE INDEX economy_market_expiry_idx
    ON launcher.economy_market_listings (server_id, expires_at)
    WHERE status = 'Active';

CREATE INDEX economy_market_delivery_player_idx
    ON launcher.economy_market_deliveries (server_id, player_uuid, created_at)
    WHERE status = 'Pending';

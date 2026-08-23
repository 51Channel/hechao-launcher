ALTER TABLE launcher.economy_products
    ADD COLUMN shop_unit_price numeric(19, 2)
        CHECK (shop_unit_price IS NULL OR shop_unit_price > 0);

ALTER TABLE launcher.economy_products
    ADD CONSTRAINT economy_products_shop_price_above_buyback_check
    CHECK (shop_unit_price IS NULL OR shop_unit_price > unit_price);

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
        'MarketClaim',
        'ShopBuy',
        'ShopClaim'
    ));

ALTER TABLE launcher.economy_product_audit
    DROP CONSTRAINT economy_product_audit_action_check;

ALTER TABLE launcher.economy_product_audit
    ADD CONSTRAINT economy_product_audit_action_check
    CHECK (action IN ('Upsert', 'Disable', 'ShopUpsert', 'ShopDisable'));

CREATE TABLE launcher.economy_shop_deliveries (
    delivery_id uuid PRIMARY KEY,
    purchase_operation_id uuid NOT NULL
        REFERENCES launcher.economy_operations(operation_id) ON DELETE RESTRICT,
    player_uuid uuid NOT NULL,
    server_id text NOT NULL
        CHECK (server_id ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    item_id text NOT NULL
        CHECK (item_id ~ '^[a-z0-9_.-]{1,64}:[a-z0-9_./-]{1,96}$'),
    quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 2304),
    unit_price numeric(19, 2) NOT NULL CHECK (unit_price > 0),
    total_amount numeric(19, 2) NOT NULL CHECK (total_amount > 0),
    status text NOT NULL DEFAULT 'Pending'
        CHECK (status IN ('Pending', 'Claimed')),
    created_at timestamp with time zone NOT NULL,
    claimed_at timestamp with time zone,
    claim_operation_id uuid
        REFERENCES launcher.economy_operations(operation_id) ON DELETE RESTRICT,
    UNIQUE (purchase_operation_id),
    CHECK (
        (status = 'Claimed' AND claimed_at IS NOT NULL AND claim_operation_id IS NOT NULL)
        OR (status = 'Pending' AND claimed_at IS NULL AND claim_operation_id IS NULL)
    )
);

CREATE INDEX economy_shop_delivery_player_idx
    ON launcher.economy_shop_deliveries (server_id, player_uuid, created_at)
    WHERE status = 'Pending';

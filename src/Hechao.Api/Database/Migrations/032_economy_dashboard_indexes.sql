CREATE INDEX economy_operations_applied_server_created_idx
    ON launcher.economy_operations (server_id, created_at DESC)
    WHERE status = 'Applied';

CREATE INDEX economy_operations_applied_created_idx
    ON launcher.economy_operations (created_at DESC, operation_kind)
    WHERE status = 'Applied';

CREATE INDEX economy_sale_quotes_committed_operation_idx
    ON launcher.economy_sale_quotes (committed_operation_id)
    WHERE status = 'Committed';

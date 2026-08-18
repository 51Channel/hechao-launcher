CREATE INDEX economy_sale_quotes_committed_item_operation_idx
    ON launcher.economy_sale_quotes (item_id, committed_operation_id)
    WHERE status = 'Committed';

package world.hechao.economy.api;

import java.math.BigDecimal;
import java.util.List;
import java.util.UUID;

public interface EconomyGateway {
    Balance balance(UUID playerUuid) throws EconomyGatewayException;

    Transfer transfer(
            String idempotencyKey,
            UUID senderUuid,
            UUID recipientUuid,
            BigDecimal amount,
            String note) throws EconomyGatewayException;

    SaleQuote quote(UUID playerUuid, String itemId, int quantity)
            throws EconomyGatewayException;

    SaleCommit commit(String idempotencyKey, UUID quoteId, UUID playerUuid)
            throws EconomyGatewayException;

    List<Product> products(boolean includeDisabled) throws EconomyGatewayException;

    Product upsertProduct(
            String itemId,
            BigDecimal unitPrice,
            int personalDailyLimit,
            int serverDailyLimit,
            UUID actorUuid,
            String actorName) throws EconomyGatewayException;

    void disableProduct(String itemId, UUID actorUuid, String actorName)
            throws EconomyGatewayException;

    boolean isConfigured();

    record Balance(UUID playerUuid, BigDecimal availableBalance, BigDecimal frozenBalance) {
    }

    record Transfer(
            UUID operationId,
            String status,
            BigDecimal senderBalance,
            BigDecimal recipientBalance,
            String failureCode) {
    }

    record SaleQuote(
            UUID quoteId,
            UUID playerUuid,
            String itemId,
            int quantity,
            BigDecimal unitPrice,
            BigDecimal totalAmount,
            int personalRemaining,
            int serverRemaining,
            java.time.Instant expiresAt) {
    }

    record SaleCommit(
            UUID operationId,
            String status,
            UUID quoteId,
            UUID playerUuid,
            String itemId,
            int quantity,
            BigDecimal amount,
            BigDecimal balance,
            String failureCode) {
    }

    record Product(
            String itemId,
            BigDecimal unitPrice,
            int personalDailyLimit,
            int serverDailyLimit,
            boolean enabled) {
    }
}

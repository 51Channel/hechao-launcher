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

    List<MarketListing> marketListings(String query) throws EconomyGatewayException;

    List<MarketListing> ownMarketListings(UUID playerUuid) throws EconomyGatewayException;

    MarketCreate marketCreate(
            String idempotencyKey,
            UUID sellerUuid,
            String sellerName,
            String itemId,
            int quantity,
            BigDecimal totalPrice) throws EconomyGatewayException;

    MarketPurchase marketPurchase(
            String idempotencyKey,
            UUID listingId,
            UUID buyerUuid,
            String buyerName) throws EconomyGatewayException;

    MarketCancel marketCancel(
            String idempotencyKey,
            UUID listingId,
            UUID sellerUuid) throws EconomyGatewayException;

    List<MarketDelivery> marketDeliveries(UUID playerUuid) throws EconomyGatewayException;

    MarketClaim marketClaim(
            String idempotencyKey,
            UUID deliveryId,
            UUID playerUuid) throws EconomyGatewayException;

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

    record MarketListing(
            UUID listingId,
            String serverId,
            UUID sellerUuid,
            String sellerName,
            String itemId,
            int quantity,
            BigDecimal totalPrice,
            BigDecimal listingFee,
            String status,
            java.time.Instant createdAt,
            java.time.Instant expiresAt) {
    }

    record MarketCreate(
            UUID operationId,
            String status,
            MarketListing listing,
            BigDecimal listingFee,
            BigDecimal balance,
            String failureCode) {
    }

    record MarketPurchase(
            UUID operationId,
            String status,
            UUID listingId,
            UUID deliveryId,
            String itemId,
            int quantity,
            BigDecimal totalPrice,
            BigDecimal sellerProceeds,
            BigDecimal transactionTax,
            BigDecimal buyerBalance,
            String failureCode) {
    }

    record MarketCancel(
            UUID operationId,
            String status,
            UUID listingId,
            UUID deliveryId,
            String itemId,
            int quantity,
            String failureCode) {
    }

    record MarketDelivery(
            UUID deliveryId,
            UUID playerUuid,
            UUID listingId,
            String serverId,
            String itemId,
            int quantity,
            String reason,
            String status,
            java.time.Instant createdAt) {
    }

    record MarketClaim(
            UUID operationId,
            String status,
            UUID deliveryId,
            String itemId,
            int quantity,
            String failureCode) {
    }
}

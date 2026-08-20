package world.hechao.economy.api;

import java.math.BigDecimal;
import java.util.List;
import java.util.UUID;

public final class UnavailableEconomyGateway implements EconomyGateway {
    private static EconomyGatewayException unavailable() {
        return new EconomyGatewayException(
                "economy service credentials are not configured",
                false,
                503);
    }

    @Override
    public Balance balance(UUID playerUuid) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public Transfer transfer(
            String idempotencyKey,
            UUID senderUuid,
            UUID recipientUuid,
            BigDecimal amount,
            String note) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public SaleQuote quote(UUID playerUuid, String itemId, int quantity)
            throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public SaleCommit commit(String idempotencyKey, UUID quoteId, UUID playerUuid)
            throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public List<Product> products(boolean includeDisabled) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public Product upsertProduct(
            String itemId,
            BigDecimal unitPrice,
            int personalDailyLimit,
            int serverDailyLimit,
            UUID actorUuid,
            String actorName) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public void disableProduct(String itemId, UUID actorUuid, String actorName)
            throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public List<MarketListing> marketListings(
            String query,
            MarketSort sort) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public List<MarketListing> ownMarketListings(UUID playerUuid)
            throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public MarketCreate marketCreate(
            String idempotencyKey,
            UUID sellerUuid,
            String sellerName,
            String itemId,
            int quantity,
            BigDecimal totalPrice) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public MarketPurchase marketPurchase(
            String idempotencyKey,
            UUID listingId,
            UUID buyerUuid,
            String buyerName) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public MarketCancel marketCancel(
            String idempotencyKey,
            UUID listingId,
            UUID sellerUuid) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public List<MarketDelivery> marketDeliveries(UUID playerUuid)
            throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public MarketClaim marketClaim(
            String idempotencyKey,
            UUID deliveryId,
            UUID playerUuid) throws EconomyGatewayException {
        throw unavailable();
    }

    @Override
    public boolean isConfigured() {
        return false;
    }
}

package world.hechao.economy.api;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.math.BigDecimal;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

final class HttpEconomyGatewayTest {
    @Test
    void queryValuePreservesNamespacedModItemPaths() {
        assertEquals(
                "example_mod%3Aparts%2Fbrass_sheet",
                HttpEconomyGateway.queryValue("example_mod:parts/brass_sheet"));
    }

    @Test
    void marketSortUsesStableApiValuesAndCycles() {
        assertEquals("recently_listed", EconomyGateway.MarketSort.RECENTLY_LISTED.apiValue());
        assertEquals(
                EconomyGateway.MarketSort.LOWEST_UNIT_PRICE,
                EconomyGateway.MarketSort.RECENTLY_LISTED.next());
        assertEquals(
                "highest_unit_price",
                EconomyGateway.MarketSort.HIGHEST_UNIT_PRICE.apiValue());
    }

    @Test
    void marketListingCalculatesAReadableUnitPrice() {
        var listing = new EconomyGateway.MarketListing(
                java.util.UUID.randomUUID(),
                "activity-survival",
                java.util.UUID.randomUUID(),
                "Seller",
                "minecraft:iron_ingot",
                64,
                new BigDecimal("125.00"),
                new BigDecimal("1.25"),
                "Active",
                java.time.Instant.now(),
                java.time.Instant.now().plusSeconds(3600));

        assertEquals(new BigDecimal("1.9531"), listing.unitPrice());
    }

    @Test
    void writeRetryReusesTheSameOperationAfterUnknownOutcome() throws Exception {
        var attempts = new AtomicInteger();

        var result = HttpEconomyGateway.retryOutcomeUnknown(() -> {
            if (attempts.incrementAndGet() == 1) {
                throw new EconomyGatewayException("response lost", true, 0);
            }
            return "applied";
        });

        assertEquals("applied", result);
        assertEquals(2, attempts.get());
    }

    @Test
    void writeRetryDoesNotRetryDefiniteRejection() {
        var attempts = new AtomicInteger();

        assertThrows(
                EconomyGatewayException.class,
                () -> HttpEconomyGateway.retryOutcomeUnknown(() -> {
                    attempts.incrementAndGet();
                    throw new EconomyGatewayException("rejected", false, 409);
                }));

        assertEquals(1, attempts.get());
    }

    @Test
    void readsOnlySafeStructuredApiErrorCodes() {
        assertEquals(
                "PERSONAL_LIMIT_EXCEEDED",
                HttpEconomyGateway.readErrorCode(
                        "{\"code\":\"PERSONAL_LIMIT_EXCEEDED\",\"message\":\"quota\"}"));
        assertNull(HttpEconomyGateway.readErrorCode("{\"message\":\"quota\"}"));
        assertNull(HttpEconomyGateway.readErrorCode("not-json"));
        assertNull(HttpEconomyGateway.readErrorCode("{\"code\":\"bad code\"}"));
    }
}

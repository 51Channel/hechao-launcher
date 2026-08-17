package world.hechao.economy.api;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

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
}

package world.hechao.economy.api;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class HttpEconomyGatewayTest {
    @Test
    void queryValuePreservesNamespacedModItemPaths() {
        assertEquals(
                "example_mod%3Aparts%2Fbrass_sheet",
                HttpEconomyGateway.queryValue("example_mod:parts/brass_sheet"));
    }
}

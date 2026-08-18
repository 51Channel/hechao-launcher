package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class EconomyMarketSearchTest {
    @Test
    void routesTheSameEncodedSearchToMarketAndCatalog() {
        var encoded = new EconomyMarketSearch.Command(
                "6ZOB6ZSP",
                "minecraft:iron_ingot");

        assertEquals(
                "hechaoeconomy:ah search 6ZOB6ZSP minecraft:iron_ingot",
                encoded.marketCommand());
        assertEquals(
                "hechaoeconomy:shop search 6ZOB6ZSP minecraft:iron_ingot",
                encoded.catalogCommand());
    }

    @Test
    void blankSearchUsesProtocolPlaceholder() {
        var encoded = EconomyMarketSearch.encode("");

        assertEquals("-", encoded.encodedQuery());
        assertEquals("-", encoded.encodedItemIds());
    }
}

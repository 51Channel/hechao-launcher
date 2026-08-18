package world.hechao.economy.gui;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.Set;
import org.junit.jupiter.api.Test;

final class MarketplaceSearchTest {
    @Test
    void fuzzySearchMatchesItemSellerAndClientTranslatedIds() {
        assertTrue(MarketplaceSearch.matches(
                "iron",
                Set.of(),
                "minecraft:iron_ingot",
                "Alice"));
        assertTrue(MarketplaceSearch.matches(
                "mci",
                Set.of(),
                "minecraft:iron_ingot",
                "Alice"));
        assertTrue(MarketplaceSearch.matches(
                "alice",
                Set.of(),
                "minecraft:iron_ingot",
                "Alice"));
        assertTrue(MarketplaceSearch.matches(
                "铁锭",
                Set.of("minecraft:iron_ingot"),
                "minecraft:iron_ingot",
                "Alice"));
        assertFalse(MarketplaceSearch.matches(
                "diamond",
                Set.of(),
                "minecraft:iron_ingot",
                "Alice"));
    }

    @Test
    void searchRequestDecodesUtf8AndRejectsInvalidIds() {
        String encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(
                "铁锭".getBytes(StandardCharsets.UTF_8));
        var request = MarketSearchRequest.decode(
                encoded,
                "minecraft:iron_ingot,Invalid:Item");

        assertTrue(request.query().equals("铁锭"));
        assertTrue(request.translatedItemIds().equals(Set.of("minecraft:iron_ingot")));
    }
}

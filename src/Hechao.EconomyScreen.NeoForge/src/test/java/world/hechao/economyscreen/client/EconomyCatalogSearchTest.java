package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyCatalogSearchTest {
    @Test
    void matchesChineseDisplayNameAndNamespacedId() {
        assertTrue(EconomyCatalogSearch.matches(
                "胡萝",
                "胡萝卜",
                "minecraft:carrot"));
        assertTrue(EconomyCatalogSearch.matches(
                "iron_ingot",
                "铁锭",
                "minecraft:iron_ingot"));
    }

    @Test
    void supportsOrderedFuzzyCharacters() {
        assertTrue(EconomyCatalogSearch.matches(
                "mccrt",
                "胡萝卜",
                "minecraft:carrot"));
        assertFalse(EconomyCatalogSearch.matches(
                "钻石",
                "胡萝卜",
                "minecraft:carrot"));
    }

    @Test
    void blankQueryKeepsEveryProduct() {
        assertTrue(EconomyCatalogSearch.matches(
                "",
                "骨头",
                "minecraft:bone"));
    }
}

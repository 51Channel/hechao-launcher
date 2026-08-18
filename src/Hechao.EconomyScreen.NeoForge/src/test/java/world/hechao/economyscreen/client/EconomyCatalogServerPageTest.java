package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class EconomyCatalogServerPageTest {
    @Test
    void parsesServerPageAndTotalCount() {
        var info = EconomyCatalogServerPage.parse("第 2 / 2 批 · 共 85 项", 40);

        assertEquals(2, info.page());
        assertEquals(2, info.pageCount());
        assertEquals(85, info.totalItemCount());
    }

    @Test
    void invalidOrLegacyLabelFallsBackToVisibleProducts() {
        var info = EconomyCatalogServerPage.parse("minecraft:paper", 45);

        assertEquals(1, info.page());
        assertEquals(1, info.pageCount());
        assertEquals(45, info.totalItemCount());
    }
}

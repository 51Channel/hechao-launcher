package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class EconomyMarketServerPageTest {
    @Test
    void parsesFilteredMarketPageAndCount() {
        var info = EconomyMarketServerPage.parse("第 2 / 3 页 · 共 103 项", 45);

        assertEquals(2, info.page());
        assertEquals(3, info.pageCount());
        assertEquals(103, info.totalItemCount());
    }

    @Test
    void invalidLabelFallsBackToVisibleOffers() {
        var info = EconomyMarketServerPage.parse("minecraft:paper", 12);

        assertEquals(1, info.page());
        assertEquals(1, info.pageCount());
        assertEquals(12, info.totalItemCount());
    }
}

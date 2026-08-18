package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyMarketLayoutTest {
    @Test
    void commonGuiUsesThreeColumnsAndSingleTabRow() {
        var layout = EconomyMarketLayout.calculate(512, 270);

        assertEquals(3, layout.columns());
        assertEquals(9, layout.pageSize());
        assertEquals(1, layout.tabRows());
        assertEquals(4, layout.tabColumns());
        assertTrue(layout.footerTop() < 270);
    }

    @Test
    void narrowGuiKeepsTabsAndCardsInsidePanel() {
        var layout = EconomyMarketLayout.calculate(200, 140);

        assertEquals(1, layout.columns());
        assertEquals(1, layout.tabRows());
        assertEquals(4, layout.tabColumns());
        assertEquals(1, layout.pageSize());
        assertTrue(layout.cardWidth() > 0);
        assertTrue(layout.contentTop() + layout.cardHeight() <= layout.footerTop());
    }

    @Test
    void minimumHeightShrinksCardWithoutCrossingFooter() {
        var layout = EconomyMarketLayout.calculate(160, 120);

        assertTrue(layout.cardHeight() >= 22);
        assertTrue(layout.cardHeight() <= EconomyMarketLayout.CARD_HEIGHT);
        assertTrue(layout.contentTop() + layout.cardHeight() <= layout.footerTop());
    }

    @Test
    void maximumPageUsesZeroBasedIndex() {
        assertEquals(0, EconomyMarketLayout.maximumPage(0, 9));
        assertEquals(0, EconomyMarketLayout.maximumPage(9, 9));
        assertEquals(1, EconomyMarketLayout.maximumPage(10, 9));
        assertEquals(4, EconomyMarketLayout.maximumPage(45, 9));
    }
}

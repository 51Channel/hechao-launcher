package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyCatalogLayoutTest {
    @Test
    void commonGuiUsesThreeColumns() {
        var layout = EconomyCatalogLayout.calculate(512, 270);

        assertEquals(3, layout.columns());
        assertEquals(12, layout.pageSize());
        assertTrue(layout.panelLeft() >= 0);
        assertTrue(layout.footerTop() < 270);
    }

    @Test
    void mediumGuiUsesTwoColumns() {
        var layout = EconomyCatalogLayout.calculate(320, 180);

        assertEquals(2, layout.columns());
        assertEquals(4, layout.pageSize());
    }

    @Test
    void narrowGuiUsesOneColumn() {
        var layout = EconomyCatalogLayout.calculate(200, 140);

        assertEquals(1, layout.columns());
        assertEquals(1, layout.pageSize());
    }

    @Test
    void maximumPageUsesZeroBasedIndex() {
        assertEquals(0, EconomyCatalogLayout.maximumPage(0, 12));
        assertEquals(0, EconomyCatalogLayout.maximumPage(12, 12));
        assertEquals(1, EconomyCatalogLayout.maximumPage(13, 12));
        assertEquals(4, EconomyCatalogLayout.maximumPage(54, 12));
    }
}

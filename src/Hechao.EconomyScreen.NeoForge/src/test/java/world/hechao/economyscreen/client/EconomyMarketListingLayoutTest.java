package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyMarketListingLayoutTest {
    @Test
    void keepsModulesAndPriceControlsSeparated() {
        var layout = layout();

        assertFalse(layout.itemModule().overlaps(layout.priceModule()));
        assertFalse(layout.inventoryModule().overlaps(layout.guideModule()));
        assertTrue(layout.itemModule().bottom() <= layout.inventoryModule().top());
        assertTrue(layout.priceModule().bottom() <= layout.inventoryModule().top());
        assertTrue(layout.priceModule().contains(layout.priceField()));
        assertTrue(layout.priceModule().contains(layout.confirmButton()));
        assertFalse(layout.priceField().overlaps(layout.confirmButton()));
        assertTrue(layout.itemModule().contains(layout.inputDock()));
        assertTrue(layout.panel().contains(layout.returnButton()));
    }

    @Test
    void leavesTextSafeZonesAroundControls() {
        var layout = layout();

        assertTrue(layout.priceField().top() >= layout.priceLabelY() + 9);
        assertTrue(layout.inventoryLabelY() + 9 < layout.inventoryModule().top());
        assertTrue(layout.itemTextWidth() >= 40);
        assertTrue(layout.guideStatusY() + 9 <= layout.guideModule().bottom() - 4);
        assertTrue(layout.guidePromptY() + 9 <= layout.guideModule().bottom() - 4);
        assertTrue(layout.inventoryModule().bottom() <= EconomyMarketListingLayout.IMAGE_HEIGHT);
        assertTrue(layout.guideModule().bottom() <= EconomyMarketListingLayout.IMAGE_HEIGHT);
    }

    @Test
    void rejectsGeometryThatCannotFitTheScreen() {
        assertThrows(
                IllegalArgumentException.class,
                () -> EconomyMarketListingLayout.calculate(277, 166, 80, 36));
        assertThrows(
                IllegalStateException.class,
                () -> EconomyMarketListingLayout.calculate(278, 166, 10, 36));
    }

    private static EconomyMarketListingLayout.Layout layout() {
        return EconomyMarketListingLayout.calculate(
                EconomyMarketListingLayout.IMAGE_WIDTH,
                EconomyMarketListingLayout.IMAGE_HEIGHT,
                80,
                36);
    }
}

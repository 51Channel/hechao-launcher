package world.hechao.economy.gui;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class MarketMenuTest {
    @Test
    void reservesBottomRowForMarketControls() {
        assertEquals(45, MarketMenu.PAGE_SIZE);
        assertEquals(45, MarketMenu.CREATE_SLOT);
        assertEquals(47, MarketMenu.BROWSE_SLOT);
        assertEquals(49, MarketMenu.PAGE_INFO_SLOT);
        assertEquals(51, MarketMenu.SORT_SLOT);
        assertEquals(53, MarketMenu.RETURN_SLOT);
    }
}

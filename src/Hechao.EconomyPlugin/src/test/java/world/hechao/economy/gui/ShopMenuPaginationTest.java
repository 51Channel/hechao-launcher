package world.hechao.economy.gui;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class ShopMenuPaginationTest {
    @Test
    void eightyFiveProductsUseTwoServerPages() {
        assertEquals(2, ShopMenuPagination.pageCount(85));
        assertEquals(0, ShopMenuPagination.firstProductIndex(0, 85));
        assertEquals(45, ShopMenuPagination.productsOnPage(0, 85));
        assertEquals(45, ShopMenuPagination.firstProductIndex(1, 85));
        assertEquals(40, ShopMenuPagination.productsOnPage(1, 85));
    }

    @Test
    void emptyAndOutOfRangePagesRemainSafe() {
        assertEquals(1, ShopMenuPagination.pageCount(0));
        assertEquals(0, ShopMenuPagination.productsOnPage(4, 0));
        assertEquals(0, ShopMenuPagination.clampPage(-1, 85));
        assertEquals(1, ShopMenuPagination.clampPage(8, 85));
    }
}

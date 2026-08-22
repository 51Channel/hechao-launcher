package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class NavigationLayoutTest {
    @Test
    void commonScaledResolutionShowsAllFifteenShortcutsWithoutPagination() {
        var layout = NavigationLayout.calculate(512, 270, 15, 0);

        assertEquals(3, layout.columns());
        assertEquals(5, layout.visibleRows());
        assertEquals(100, layout.buttonWidth());
        assertEquals(312, layout.gridWidth());
        assertFalse(layout.needsNavigation());
        assertEquals((512 - 312) / 2, layout.gridLeft());
    }

    @Test
    void mediumResolutionUsesCompactTwoColumnGrid() {
        var layout = NavigationLayout.calculate(320, 180, 15, 0);

        assertEquals(2, layout.columns());
        assertTrue(layout.visibleRows() >= 1);
        assertEquals(106, layout.buttonWidth());
        assertTrue(layout.needsNavigation());
    }

    @Test
    void retainsAllActionsThroughPaginationOnSmallWindows() {
        var firstPage = NavigationLayout.calculate(240, 160, 15, 0);
        var lastPage = NavigationLayout.calculate(240, 160, 15, 99);

        assertEquals(1, firstPage.columns());
        assertTrue(firstPage.needsNavigation());
        assertTrue(firstPage.visibleRows() >= 1);
        assertEquals(firstPage.maximumScrollRow(), lastPage.scrollRow());
        assertEquals(15, firstPage.totalRows());
    }

    @Test
    void keepsButtonsBelowTheIndustrialHeaderOnShortWindows() {
        var layout = NavigationLayout.calculate(240, 100, 6, 0);

        assertTrue(layout.gridTop() >= layout.titleTop() + 22);
        assertTrue(layout.sharedFooter());
        assertEquals(layout.navigationTop(), layout.returnTop());
    }

    @Test
    void rejectsInvalidLayoutInputs() {
        org.junit.jupiter.api.Assertions.assertThrows(
                IllegalArgumentException.class,
                () -> NavigationLayout.calculate(0, 270, 6, 0));
        org.junit.jupiter.api.Assertions.assertThrows(
                IllegalArgumentException.class,
                () -> NavigationLayout.calculate(512, 270, 0, 0));
    }
}

package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class NavigationLayoutTest {
    @Test
    void matchesCompactTwoColumnReferenceAtCommonScaledResolution() {
        var layout = NavigationLayout.calculate(512, 270, 6, 0);

        assertEquals(2, layout.columns());
        assertEquals(3, layout.visibleRows());
        assertEquals(158, layout.buttonWidth());
        assertEquals(322, layout.gridWidth());
        assertFalse(layout.needsNavigation());
        assertEquals((512 - 322) / 2, layout.gridLeft());
    }

    @Test
    void retainsAllActionsThroughPaginationOnSmallWindows() {
        var firstPage = NavigationLayout.calculate(320, 160, 6, 0);
        var lastPage = NavigationLayout.calculate(320, 160, 6, 99);

        assertEquals(1, firstPage.columns());
        assertTrue(firstPage.needsNavigation());
        assertTrue(firstPage.visibleRows() >= 1);
        assertEquals(firstPage.maximumScrollRow(), lastPage.scrollRow());
        assertEquals(6, firstPage.totalRows());
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

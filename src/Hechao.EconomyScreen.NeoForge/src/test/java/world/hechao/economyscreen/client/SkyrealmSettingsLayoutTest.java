package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class SkyrealmSettingsLayoutTest {
    @Test
    void keepsThreeRowsAndFooterInsideStandardScreen() {
        var layout = SkyrealmSettingsLayout.calculate(426, 240);

        assertTrue(layout.rowHeight() >= SkyrealmSettingsLayout.TOGGLE_HEIGHT);
        for (int index = 0; index < 3; index++) {
            assertTrue(layout.rowTop(index) >= layout.rowsTop());
            assertTrue(layout.toggleY(index) + SkyrealmSettingsLayout.TOGGLE_HEIGHT
                    <= layout.footerY());
        }
        assertTrue(layout.footerY() + SkyrealmSettingsLayout.RETURN_HEIGHT
                <= layout.panelTop() + layout.panelHeight());
    }

    @Test
    void remainsPositiveOnSmallClientWindow() {
        var layout = SkyrealmSettingsLayout.calculate(220, 110);

        assertTrue(layout.panelWidth() > 0);
        assertTrue(layout.panelHeight() > 0);
        assertTrue(layout.rowHeight() > 0);
        assertTrue(layout.footerY() >= layout.panelTop());
    }
}

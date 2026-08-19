package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class TeamManagementLayoutTest {
    @Test
    void usesTwoColumnsOnCommonGuiSizes() {
        var layout = TeamManagementLayout.calculate(512, 288);

        assertTrue(layout.twoColumns());
        assertTrue(layout.controlsLeft()
                >= layout.contentLeft() + layout.summaryWidth());
        assertTrue(layout.controlsTop() + requiredControlHeight()
                <= layout.footerY());
    }

    @Test
    void stacksWithoutOverlappingFooterOnNarrowGui() {
        var layout = TeamManagementLayout.calculate(320, 240);

        assertFalse(layout.twoColumns());
        assertTrue(layout.controlsLeft() == layout.contentLeft());
        assertTrue(layout.controlsTop() + requiredControlHeight()
                <= layout.footerY());
        assertTrue(layout.panelLeft() >= 0);
        assertTrue(layout.panelTop() >= 0);
    }

    private static int requiredControlHeight() {
        return TeamManagementLayout.BUTTON_HEIGHT * 3
                + TeamManagementLayout.GAP * 2;
    }
}

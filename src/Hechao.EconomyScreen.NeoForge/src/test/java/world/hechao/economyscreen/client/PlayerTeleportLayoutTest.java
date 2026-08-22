package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class PlayerTeleportLayoutTest {
    @Test
    void commonResolutionUsesAReadableTwoByTwoActionGrid() {
        var layout = PlayerTeleportLayout.calculate(512, 270);

        assertFalse(layout.singleActionRow());
        assertTrue(layout.actionX(1) > layout.actionX(0));
        assertTrue(layout.actionY(2) > layout.actionY(0));
        assertTrue(layout.statusTop() >= layout.actionY(3) + layout.buttonHeight());
        assertTrue(layout.statusTop() + layout.statusHeight() <= layout.returnY());
    }

    @Test
    void shortScreensKeepAllActionsInOneRowAboveTheFooter() {
        var layout = PlayerTeleportLayout.calculate(240, 100);

        assertTrue(layout.singleActionRow());
        assertTrue(layout.actionX(3) > layout.actionX(2));
        assertTrue(layout.statusHeight() >= 16);
        assertTrue(layout.actionY(3) + layout.buttonHeight() <= layout.returnY());
        assertTrue(layout.statusTop() + layout.statusHeight() <= layout.returnY());
        assertTrue(layout.returnY() + layout.returnHeight() <= 100);
    }
}

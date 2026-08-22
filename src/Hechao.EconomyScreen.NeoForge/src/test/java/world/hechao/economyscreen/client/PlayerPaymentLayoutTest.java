package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class PlayerPaymentLayoutTest {
    @Test
    void keepsFieldsActionsStatusAndFooterSeparatedAtCommonResolution() {
        var layout = PlayerPaymentLayout.calculate(512, 270);

        assertTrue(layout.amountLeft() >= layout.contentLeft() + layout.playerWidth());
        assertTrue(layout.actionY() >= layout.fieldTop() + layout.fieldHeight());
        assertTrue(layout.statusTop() >= layout.actionY() + layout.buttonHeight());
        assertTrue(layout.statusTop() + layout.statusHeight() <= layout.returnY());
        assertTrue(layout.returnY() + layout.returnHeight()
                <= layout.panelTop() + layout.panelHeight());
    }

    @Test
    void compactLayoutStaysInsideTheScreenWithoutNegativeDimensions() {
        var layout = PlayerPaymentLayout.calculate(240, 100);

        assertTrue(layout.compact());
        assertTrue(layout.panelLeft() >= 0);
        assertTrue(layout.panelTop() >= 0);
        assertTrue(layout.playerWidth() > 0);
        assertTrue(layout.amountWidth() > 0);
        assertTrue(layout.statusHeight() >= 16);
        assertTrue(layout.statusTop() + layout.statusHeight() <= layout.returnY());
        assertTrue(layout.returnY() + layout.returnHeight() <= 100);
    }
}

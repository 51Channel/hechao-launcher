package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyResultLayoutTest {
    @Test
    void providesDetailedInstrumentAtCommonScaledResolution() {
        var layout = EconomyResultLayout.calculate(394, 172);

        assertTrue(layout.detailed());
        assertTrue(layout.contentWidth() >= 240);
        assertTrue(layout.contentHeight() >= 62);
        assertTrue(layout.contentTop() + layout.contentHeight() < layout.buttonY());
        assertTrue(layout.buttonY() + EconomyResultLayout.BUTTON_HEIGHT
                <= layout.panelTop() + layout.panelHeight());
    }

    @Test
    void fallsBackToCompactResultWithoutOverlappingButtons() {
        var layout = EconomyResultLayout.calculate(220, 110);

        assertFalse(layout.detailed());
        assertTrue(layout.contentHeight() >= 0);
        assertTrue(layout.contentTop() + layout.contentHeight() < layout.buttonY());
        assertTrue(layout.buttonY() + EconomyResultLayout.BUTTON_HEIGHT
                <= layout.panelTop() + layout.panelHeight());
    }

    @Test
    void rejectsInvalidScreenDimensions() {
        assertThrows(
                IllegalArgumentException.class,
                () -> EconomyResultLayout.calculate(0, 172));
    }
}

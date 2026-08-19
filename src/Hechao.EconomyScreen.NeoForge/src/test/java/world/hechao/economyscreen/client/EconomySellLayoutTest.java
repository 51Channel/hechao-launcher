package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomySellLayoutTest {
    @Test
    void usesNativeServerControlsWhenCompactScreenHasNoSafeFooter() {
        var layout = layoutFor(320, 180);

        assertFalse(layout.customControls());
    }

    @Test
    void placesCustomControlsBelowContainerAtMediumResolution() {
        var layout = layoutFor(426, 240);

        assertTrue(layout.customControls());
        assertTrue(layout.buttonY() >= 37 + 166);
        assertTrue(layout.confirmX() >= 0);
        assertTrue(layout.returnX() + EconomySellLayout.BUTTON_WIDTH <= 426);
    }

    @Test
    void placesCustomControlsBelowContainerAtWideResolution() {
        var layout = layoutFor(854, 480);

        assertTrue(layout.customControls());
        assertTrue(layout.buttonY() >= 157 + 166);
        assertTrue(layout.confirmX() >= 0);
        assertTrue(layout.returnX() + EconomySellLayout.BUTTON_WIDTH <= 854);
    }

    private static EconomySellLayout.Layout layoutFor(int width, int height) {
        int containerWidth = 176;
        int containerHeight = 166;
        int left = (width - containerWidth) / 2;
        int top = (height - containerHeight) / 2;
        return EconomySellLayout.calculate(
                width,
                height,
                left,
                top,
                containerWidth,
                containerHeight);
    }
}

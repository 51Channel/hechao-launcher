package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.awt.image.BufferedImage;
import java.io.IOException;
import javax.imageio.ImageIO;
import org.junit.jupiter.api.Test;

final class IndustrialUiThemeTest {
    @Test
    void preservesTheWholeBackdropAtSixteenByNine() {
        assertEquals(
                new BackdropCover.Crop(0, 0, 1024, 576),
                BackdropCover.calculate(512, 288, 1024, 576));
    }

    @Test
    void cropsTheSidesForPortraitScreens() {
        assertEquals(
                new BackdropCover.Crop(320, 0, 384, 576),
                BackdropCover.calculate(320, 480, 1024, 576));
    }

    @Test
    void cropsTheTopAndBottomForUltrawideScreens() {
        assertEquals(
                new BackdropCover.Crop(0, 72, 1024, 432),
                BackdropCover.calculate(640, 270, 1024, 576));
    }

    @Test
    void rejectsInvalidScreenDimensions() {
        assertThrows(
                IllegalArgumentException.class,
                () -> BackdropCover.calculate(0, 270, 1024, 576));
    }

    @Test
    void packagesValidatedImage2Textures() throws IOException {
        BufferedImage backdrop = readTexture("industrial_backdrop.png");
        assertEquals(1024, backdrop.getWidth());
        assertEquals(576, backdrop.getHeight());

        BufferedImage emblem = readTexture("expedition_emblem.png");
        assertEquals(128, emblem.getWidth());
        assertEquals(128, emblem.getHeight());
        assertTrue(emblem.getColorModel().hasAlpha());
        assertEquals(0, emblem.getRGB(0, 0) >>> 24);
    }

    private static BufferedImage readTexture(String name) throws IOException {
        try (var stream = IndustrialUiThemeTest.class.getResourceAsStream(
                "/assets/hechao_economy_screen/textures/gui/" + name)) {
            assertNotNull(stream);
            BufferedImage image = ImageIO.read(stream);
            assertNotNull(image);
            return image;
        }
    }
}

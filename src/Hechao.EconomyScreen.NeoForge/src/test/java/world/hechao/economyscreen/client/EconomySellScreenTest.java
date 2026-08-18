package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class EconomySellScreenTest {
    private static final Path SOURCE = Path.of(
            "src",
            "main",
            "java",
            "world",
            "hechao",
            "economyscreen",
            "client",
            "EconomySellScreen.java");

    @Test
    void preservesNativeContainerInteractionAndUsesServerControls() throws IOException {
        var source = Files.readString(SOURCE);

        assertTrue(source.contains("extends ContainerScreen"));
        assertTrue(source.contains("handleInventoryMouseClick("));
        assertTrue(source.contains("menu.containerId"));
        assertTrue(source.contains("CONFIRM_SLOT"));
        assertTrue(source.contains("RETURN_SLOT"));
    }
}

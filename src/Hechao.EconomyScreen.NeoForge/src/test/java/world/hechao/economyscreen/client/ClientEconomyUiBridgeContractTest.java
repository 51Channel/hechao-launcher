package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class ClientEconomyUiBridgeContractTest {
    private static final Path SOURCE = Path.of(
            "src",
            "main",
            "java",
            "world",
            "hechao",
            "economyscreen",
            "client",
            "ClientEconomyUiBridge.java");

    @Test
    void ownsSkyrealmSettingsContainerAndKeepsMarketScreens() throws IOException {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("SETTINGS_TITLE = \"天域设置\""));
        assertTrue(source.contains("new SkyrealmSettingsScreen(container.getMenu())"));
        assertTrue(source.contains("new EconomyMarketScreen("));
        assertTrue(source.contains("new EconomyMarketListingScreen("));
        assertTrue(source.contains("new TeamManagementScreen("));
    }
}

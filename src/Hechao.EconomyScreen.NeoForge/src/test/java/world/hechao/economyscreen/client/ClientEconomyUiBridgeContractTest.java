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
        assertTrue(source.contains("new PlayerPaymentScreen("));
        assertTrue(source.contains("new PlayerTeleportScreen("));
        assertTrue(source.contains("EconomyMessageProtocol.isMenuSessionReceipt(message)"));
        assertTrue(source.contains("event.setCanceled(true)"));
    }

    @Test
    void routesOnlyMenuLifecycleErrorsIntoTheTeamWaitingScreen()
            throws IOException {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("minecraft.screen instanceof TeamManagementScreen screen"));
        assertTrue(source.contains("isMenuActionError(message)"));
        assertTrue(source.contains("message.contains(\"菜单已失效\")"));
        assertTrue(source.contains("message.contains(\"操作太快\")"));
        assertTrue(source.contains("message.contains(\"当前功能不可用\")"));
    }

    @Test
    void routesPaymentAndTeleportResponsesOnlyToTheirActiveScreens()
            throws IOException {
        String source = Files.readString(SOURCE);

        assertTrue(source.contains("minecraft.screen instanceof PlayerPaymentScreen screen"));
        assertTrue(source.contains("screen.acceptsSystemMessage(message)"));
        assertTrue(source.contains("minecraft.screen instanceof PlayerTeleportScreen screen"));
        assertTrue(source.contains("EconomyMessageProtocol.isMenuSessionReceipt(message)"));
        assertTrue(source.contains("screen.acceptMessage(event.getMessage())"));
    }
}

package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;

final class HomeNavigationContractTest {
    private static final Path SOURCE_ROOT = Path.of(
            "src",
            "main",
            "java",
            "world",
            "hechao",
            "economyscreen",
            "client");

    @Test
    void containerScreensStayVisibleUntilTheHomePayloadArrives() throws Exception {
        for (var fileName : List.of(
                "EconomyCatalogScreen.java",
                "EconomySellScreen.java",
                "EconomyMarketScreen.java",
                "SkyrealmSettingsScreen.java")) {
            String method = methodBody(
                    Files.readString(SOURCE_ROOT.resolve(fileName)),
                    "private void returnHome()");

            assertTrue(method.contains("ClientEconomyUiBridge.requestHome()"), fileName);
            assertFalse(method.contains("closeContainer()"), fileName);
            assertFalse(method.contains("clickControl("), fileName);
            assertFalse(method.contains("clickServerSlot("), fileName);
        }
    }

    @Test
    void homeRequestNeverClearsTheCurrentScreenLocally() throws Exception {
        String source = Files.readString(SOURCE_ROOT.resolve("ClientEconomyUiBridge.java"));
        String method = methodBody(source, "static void requestHome()");
        String closeMethod = methodBody(
                source,
                "private static void closeOpenContainerWithoutClosingScreen()");

        assertTrue(method.contains("closeOpenContainerWithoutClosingScreen()"));
        assertTrue(method.contains("sendCommand(\"hechaomenu economy\")"));
        assertFalse(method.contains("setScreen("));
        assertFalse(method.contains("closeContainer("));
        assertTrue(closeMethod.contains("ServerboundContainerClosePacket"));
        assertTrue(closeMethod.contains("player.containerMenu = player.inventoryMenu"));
        assertFalse(closeMethod.contains("setScreen("));
        assertFalse(closeMethod.contains("closeContainer("));
    }

    private static String methodBody(String source, String signature) {
        int start = source.indexOf(signature);
        if (start < 0) {
            throw new AssertionError("missing method: " + signature);
        }
        int open = source.indexOf('{', start);
        int depth = 0;
        for (int index = open; index < source.length(); index++) {
            char value = source.charAt(index);
            if (value == '{') {
                depth++;
            } else if (value == '}' && --depth == 0) {
                return source.substring(open, index + 1);
            }
        }
        throw new AssertionError("unterminated method: " + signature);
    }
}

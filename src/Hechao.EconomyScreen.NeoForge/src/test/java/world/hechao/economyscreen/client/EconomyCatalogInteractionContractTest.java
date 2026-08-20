package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class EconomyCatalogInteractionContractTest {
    @Test
    void catalogCardsOpenTheOfficialBuybackWorkflow() throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economyscreen",
                "client",
                "EconomyCatalogScreen.java"));

        assertTrue(source.contains("public boolean mouseClicked("));
        assertTrue(source.contains("productAt(mouseX, mouseY)"));
        assertTrue(source.contains("ClientEconomyUiBridge.requestOfficialBuyback()"));
        assertTrue(source.contains("\"回收 >\""));
        assertTrue(source.contains("OPEN_BUYBACK_COOLDOWN_TICKS"));
    }
}

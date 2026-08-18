package world.hechao.economy.commands;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.List;
import org.junit.jupiter.api.Test;

final class ProductAdminPromptTest {
    @Test
    void exposesStableOneClickPriceCommands() {
        assertEquals(
                List.of(
                        "/hechaoeconomy:heco product set 1.00",
                        "/hechaoeconomy:heco product set 5.00",
                        "/hechaoeconomy:heco product set 10.00",
                        "/hechaoeconomy:heco product set 25.00",
                        "/hechaoeconomy:heco product set 50.00",
                        "/hechaoeconomy:heco product set 100.00"),
                ProductAdminPrompt.presetCommands());
    }

    @Test
    void doesNotLinkAdventureClassesMissingFromArclight() throws IOException {
        try (var stream = ProductAdminPrompt.class.getResourceAsStream("ProductAdminPrompt.class")) {
            var bytecode = new String(stream.readAllBytes(), StandardCharsets.ISO_8859_1);
            assertFalse(bytecode.contains("net/kyori/adventure"));
        }
    }
}

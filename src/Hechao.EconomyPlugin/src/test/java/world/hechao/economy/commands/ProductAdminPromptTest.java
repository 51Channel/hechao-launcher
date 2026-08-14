package world.hechao.economy.commands;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.util.List;
import org.junit.jupiter.api.Test;

final class ProductAdminPromptTest {
    @Test
    void exposesStableOneClickPriceCommands() {
        assertEquals(
                List.of(
                        "/heco product set 1.00",
                        "/heco product set 5.00",
                        "/heco product set 10.00",
                        "/heco product set 25.00",
                        "/heco product set 50.00",
                        "/heco product set 100.00"),
                ProductAdminPrompt.presetCommands());
    }
}

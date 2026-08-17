package world.hechao.economy;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class PluginDescriptorContractTest {
    @Test
    void loadsBeforeEssentialsAndOwnsEconomyCommands() throws Exception {
        var descriptor = Files.readString(Path.of(
                "src",
                "main",
                "resources",
                "plugin.yml"));

        assertTrue(descriptor.contains("loadbefore:\n  - Essentials"));
        for (var command : java.util.List.of("money", "pay", "sell", "shop", "heco")) {
            assertTrue(descriptor.contains("  " + command + ":"));
        }
    }
}

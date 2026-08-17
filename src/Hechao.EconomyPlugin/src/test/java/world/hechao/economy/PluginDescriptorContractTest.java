package world.hechao.economy;

import static org.junit.jupiter.api.Assertions.assertFalse;
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

        assertTrue(descriptor.contains("version: '0.1.5'"));
        assertTrue(descriptor.contains("softdepend:\n  - PlaceholderAPI\n  - Essentials"));
        assertFalse(descriptor.contains("loadbefore:"));
        for (var command : java.util.List.of("money", "pay", "sell", "shop", "heco")) {
            assertTrue(descriptor.contains("  " + command + ":"));
        }
    }
}

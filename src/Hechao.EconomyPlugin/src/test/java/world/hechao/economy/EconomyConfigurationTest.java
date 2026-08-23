package world.hechao.economy;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import java.nio.file.Path;
import org.bukkit.configuration.file.FileConfiguration;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

final class EconomyConfigurationTest {
    @TempDir
    Path temporaryDirectory;

    @Test
    void absentTokenKeepsConfigurationFailClosed() throws Exception {
        var config = validConfiguration();

        var result = EconomyConfiguration.load(config, temporaryDirectory);

        assertFalse(result.isConfigured());
        assertEquals("activity-survival", result.serverId());
    }

    @Test
    void rejectsHttpApiOrigin() {
        var config = validConfiguration();
        when(config.getString("api-base-url")).thenReturn("http://example.test");

        assertThrows(
                IllegalArgumentException.class,
                () -> EconomyConfiguration.load(config, temporaryDirectory));
    }

    @Test
    void rejectsTokenPathOutsideServerRoot() {
        var config = validConfiguration();
        when(config.getString("token-file")).thenReturn("../secret.txt");

        assertThrows(
                IllegalArgumentException.class,
                () -> EconomyConfiguration.load(config, temporaryDirectory));
    }

    @Test
    void failureDefaultsRemainUnconfigured() {
        var result = EconomyConfiguration.failClosedDefaults();

        assertFalse(result.isConfigured());
        assertEquals("activity-survival", result.serverId());
        assertEquals(15, result.balanceCacheLifetime().toSeconds());
    }

    private static FileConfiguration validConfiguration() {
        var config = mock(FileConfiguration.class);
        when(config.getString("api-base-url"))
                .thenReturn("https://launcher-api.hechao.world");
        when(config.getString("server-id")).thenReturn("activity-survival");
        when(config.getString("token-environment-variable"))
                .thenReturn("HECHAO_ECONOMY_TEST_TOKEN_DO_NOT_SET");
        when(config.getString("token-file"))
                .thenReturn("plugins/HechaoEconomy/economy-token.txt");
        when(config.getInt("request-timeout-seconds", 0)).thenReturn(3);
        when(config.getInt("balance-cache-seconds", 0)).thenReturn(15);
        when(config.getDouble("pay-confirm-threshold", 10_000.0)).thenReturn(10_000.0);
        when(config.getInt("default-personal-daily-limit", 0)).thenReturn(2304);
        when(config.getInt("default-server-daily-limit", 2303)).thenReturn(23040);
        return config;
    }
}

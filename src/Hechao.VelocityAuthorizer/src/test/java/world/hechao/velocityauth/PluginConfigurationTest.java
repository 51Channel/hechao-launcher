package world.hechao.velocityauth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

final class PluginConfigurationTest {
    @TempDir
    Path tempDirectory;

    @Test
    void createsSafeMonitorConfiguration() throws Exception {
        PluginConfiguration configuration = PluginConfiguration.load(tempDirectory);

        assertEquals(AuthorizationMode.MONITOR, configuration.mode());
        assertEquals("https", configuration.apiUri().getScheme());
        assertFalse(configuration.hasCredential());
        assertTrue(Files.exists(tempDirectory.resolve("config.properties")));
    }

    @Test
    void loadsConfiguredCredential() throws Exception {
        Files.writeString(
                tempDirectory.resolve("config.properties"),
                """
                mode=enforce
                api-url=https://launcher-api.hechao.world/v1/internal/velocity/authorize
                token=abcdefghijklmnopqrstuvwxyz012345
                proxy-instance=owl5-main
                request-timeout-millis=1800
                """,
                StandardCharsets.UTF_8);

        PluginConfiguration configuration = PluginConfiguration.load(tempDirectory);

        assertEquals(AuthorizationMode.ENFORCE, configuration.mode());
        assertTrue(configuration.hasCredential());
        assertEquals(1800, configuration.requestTimeout().toMillis());
    }

    @Test
    void rejectsInsecureRemoteApi() throws Exception {
        Files.writeString(
                tempDirectory.resolve("config.properties"),
                """
                mode=monitor
                api-url=http://launcher-api.hechao.world/v1/internal/velocity/authorize
                token=abcdefghijklmnopqrstuvwxyz012345
                proxy-instance=owl5-main
                request-timeout-millis=1800
                """,
                StandardCharsets.UTF_8);

        assertThrows(
                IllegalArgumentException.class,
                () -> PluginConfiguration.load(tempDirectory));
    }
}

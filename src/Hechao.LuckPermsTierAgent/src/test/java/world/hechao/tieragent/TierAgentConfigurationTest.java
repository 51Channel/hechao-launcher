package world.hechao.tieragent;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.nio.file.Files;
import java.time.Duration;
import org.junit.jupiter.api.Test;

class TierAgentConfigurationTest {
    @Test
    void loadsRestrictedHttpsConfiguration() throws Exception {
        var path = Files.createTempFile("hechao-tier-agent", ".properties");
        try {
            Files.writeString(path, """
                    api-base-url=https://launcher-api.hechao.world/
                    token=abcdefghijklmnopqrstuvwxyzABCDEF0123456789
                    agent-id=owl5-lobby
                    request-timeout-seconds=7
                    poll-interval-seconds=11
                    claim-limit=4
                    """);

            var configuration = TierAgentConfiguration.load(path);

            assertEquals("owl5-lobby", configuration.agentId());
            assertEquals(Duration.ofSeconds(7), configuration.requestTimeout());
            assertEquals(Duration.ofSeconds(11), configuration.pollInterval());
            assertEquals(4, configuration.claimLimit());
            assertEquals(
                    "https://launcher-api.hechao.world/"
                            + "v1/internal/luckperms/tier-commands/claim",
                    configuration.claimUri().toString());
        } finally {
            Files.deleteIfExists(path);
        }
    }

    @Test
    void rejectsPlainHttp() throws Exception {
        var path = Files.createTempFile("hechao-tier-agent", ".properties");
        try {
            Files.writeString(path, """
                    api-base-url=http://launcher-api.hechao.world/
                    token=abcdefghijklmnopqrstuvwxyzABCDEF0123456789
                    agent-id=owl5-lobby
                    request-timeout-seconds=7
                    poll-interval-seconds=11
                    claim-limit=4
                    """);

            assertThrows(
                    IllegalArgumentException.class,
                    () -> TierAgentConfiguration.load(path));
        } finally {
            Files.deleteIfExists(path);
        }
    }
}

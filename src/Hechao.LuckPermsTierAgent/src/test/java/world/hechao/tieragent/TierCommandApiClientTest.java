package world.hechao.tieragent;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.net.URI;
import java.net.http.HttpClient;
import java.time.Duration;
import java.util.UUID;
import org.junit.jupiter.api.Test;

final class TierCommandApiClientTest {
    private final TierCommandApiClient client = new TierCommandApiClient(
            new TierAgentConfiguration(
                    URI.create("https://launcher-api.hechao.world/"),
                    "a".repeat(32),
                    "owl5-lobby",
                    Duration.ofSeconds(10),
                    Duration.ofSeconds(10),
                    10),
            HttpClient.newHttpClient());

    @Test
    void claimPayloadCarriesSoftwareAndProtocolVersion() {
        var request = client.claimRequest();

        assertEquals("owl5-lobby", request.agentId());
        assertEquals("0.1.3", request.agentVersion());
        assertEquals(2, request.protocolVersion());
        assertEquals(10, request.limit());
    }

    @Test
    void completionPayloadCarriesSameVersionFence() {
        var command = new TierCommand(
                UUID.randomUUID(),
                UUID.randomUUID(),
                "default",
                "vip",
                "Participant",
                3);

        var request = client.completionRequest(
                command,
                TierMutationResult.applied("vip"));

        assertEquals("owl5-lobby", request.agentId());
        assertEquals("0.1.3", request.agentVersion());
        assertEquals(2, request.protocolVersion());
        assertEquals(3, request.attemptCount());
        assertEquals("Applied", request.outcome());
        assertEquals("vip", request.observedPrimaryGroup());
        assertNull(request.failureCode());
    }
}

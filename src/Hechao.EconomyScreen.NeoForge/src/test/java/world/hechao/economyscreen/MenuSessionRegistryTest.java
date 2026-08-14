package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.time.Duration;
import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.Test;

final class MenuSessionRegistryTest {
    private final MenuSessionRegistry registry = new MenuSessionRegistry(
            Duration.ofMinutes(2),
            Duration.ofMillis(350));

    @Test
    void acceptsMatchingSessionAndRateLimitsRepeatedAction() {
        var player = UUID.randomUUID();
        var now = Instant.parse("2026-08-14T03:00:00Z");
        var session = registry.issue(player, now);

        assertEquals(
                MenuSessionRegistry.Validation.ALLOWED,
                registry.validateAndConsume(player, session, now.plusSeconds(1)));
        assertEquals(
                MenuSessionRegistry.Validation.RATE_LIMITED,
                registry.validateAndConsume(player, session, now.plusMillis(1100)));
        assertEquals(
                MenuSessionRegistry.Validation.ALLOWED,
                registry.validateAndConsume(player, session, now.plusMillis(1400)));
    }

    @Test
    void rejectsOtherPlayerAndExpiredSession() {
        var player = UUID.randomUUID();
        var now = Instant.parse("2026-08-14T03:00:00Z");
        var session = registry.issue(player, now);

        assertEquals(
                MenuSessionRegistry.Validation.MISSING,
                registry.validateAndConsume(UUID.randomUUID(), session, now.plusSeconds(1)));
        assertEquals(
                MenuSessionRegistry.Validation.EXPIRED,
                registry.validateAndConsume(
                        player,
                        session,
                        now.plus(Duration.ofMinutes(3))));
    }
}

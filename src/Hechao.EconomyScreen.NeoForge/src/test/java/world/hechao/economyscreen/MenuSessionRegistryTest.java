package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.time.Duration;
import java.time.Instant;
import java.util.Set;
import java.util.UUID;
import org.junit.jupiter.api.Test;

final class MenuSessionRegistryTest {
    private final MenuSessionRegistry registry = new MenuSessionRegistry(
            Duration.ofMinutes(2),
            Duration.ofMillis(350));

    @Test
    void consumesMatchingSessionAfterOneAction() {
        var player = UUID.randomUUID();
        var now = Instant.parse("2026-08-14T03:00:00Z");
        var session = registry.issue(player, Set.of("balance"), now);

        assertEquals(
                MenuSessionRegistry.Validation.ALLOWED,
                registry.validateAndConsume(
                        player,
                        session,
                        "balance",
                        now.plusSeconds(1)));
        assertEquals(
                MenuSessionRegistry.Validation.MISSING,
                registry.validateAndConsume(
                        player,
                        session,
                        "balance",
                        now.plusMillis(1400)));
    }

    @Test
    void rejectsOtherPlayerAndExpiredSession() {
        var player = UUID.randomUUID();
        var now = Instant.parse("2026-08-14T03:00:00Z");
        var session = registry.issue(player, Set.of("balance"), now);

        assertEquals(
                MenuSessionRegistry.Validation.MISSING,
                registry.validateAndConsume(
                        UUID.randomUUID(),
                        session,
                        "balance",
                        now.plusSeconds(1)));
        assertEquals(
                MenuSessionRegistry.Validation.EXPIRED,
                registry.validateAndConsume(
                        player,
                        session,
                        "balance",
                        now.plus(Duration.ofMinutes(3))));
    }

    @Test
    void rejectsActionThatWasNotGrantedAndKeepsSessionForTheGrantedAction() {
        var player = UUID.randomUUID();
        var now = Instant.parse("2026-08-14T03:00:00Z");
        var session = registry.issue(player, Set.of("balance"), now);

        assertEquals(
                MenuSessionRegistry.Validation.ACTION_NOT_ALLOWED,
                registry.validateAndConsume(
                        player,
                        session,
                        "admin_product",
                        now.plusSeconds(1)));
        assertEquals(1, registry.activeSessionCount());
        assertEquals(
                MenuSessionRegistry.Validation.ALLOWED,
                registry.validateAndConsume(
                        player,
                        session,
                        "balance",
                        now.plusSeconds(2)));
        assertEquals(0, registry.activeSessionCount());
    }

    @Test
    void rateLimitsActionsAcrossFreshSessions() {
        var player = UUID.randomUUID();
        var now = Instant.parse("2026-08-14T03:00:00Z");
        var first = registry.issue(player, Set.of("balance"), now);
        assertEquals(
                MenuSessionRegistry.Validation.ALLOWED,
                registry.validateAndConsume(
                        player,
                        first,
                        "balance",
                        now.plusSeconds(1)));

        var second = registry.issue(player, Set.of("balance"), now.plusMillis(1100));
        assertEquals(
                MenuSessionRegistry.Validation.RATE_LIMITED,
                registry.validateAndConsume(
                        player,
                        second,
                        "balance",
                        now.plusMillis(1200)));
        assertEquals(1, registry.activeSessionCount());
        assertEquals(
                MenuSessionRegistry.Validation.ALLOWED,
                registry.validateAndConsume(
                        player,
                        second,
                        "balance",
                        now.plusMillis(1500)));
    }
}

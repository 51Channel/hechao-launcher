package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Duration;
import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.Test;

final class RtpCooldownRegistryTest {
    @Test
    void blocksRepeatedTeleportsUntilCooldownExpires() {
        var registry = new RtpCooldownRegistry(Duration.ofSeconds(60));
        var player = UUID.fromString("c7c74773-ecfa-43f8-b693-d83aa38494fe");
        var now = Instant.parse("2026-08-22T12:00:00Z");

        assertTrue(registry.tryAcquire(player, now).allowed());
        var blocked = registry.tryAcquire(player, now.plusSeconds(15));
        assertFalse(blocked.allowed());
        assertEquals(Duration.ofSeconds(45), blocked.remaining());
        assertTrue(registry.tryAcquire(player, now.plusSeconds(60)).allowed());
    }

    @Test
    void releasesReservationWhenTeleportFails() {
        var registry = new RtpCooldownRegistry(Duration.ofSeconds(60));
        var player = UUID.fromString("bf8ef00d-b564-4497-b290-b76c005796ba");
        var now = Instant.parse("2026-08-22T12:00:00Z");

        assertTrue(registry.tryAcquire(player, now).allowed());
        registry.release(player);
        assertTrue(registry.tryAcquire(player, now).allowed());
    }
}

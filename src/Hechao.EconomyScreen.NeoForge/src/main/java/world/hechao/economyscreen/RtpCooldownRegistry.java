package world.hechao.economyscreen;

import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.UUID;

final class RtpCooldownRegistry {
    private final Duration cooldown;
    private final HashMap<UUID, Instant> expiresAtByPlayer = new HashMap<>();

    RtpCooldownRegistry(Duration cooldown) {
        if (cooldown == null || cooldown.isNegative() || cooldown.isZero()) {
            throw new IllegalArgumentException("cooldown must be positive");
        }
        this.cooldown = cooldown;
    }

    synchronized Attempt tryAcquire(UUID playerUuid, Instant now) {
        if (playerUuid == null || now == null) {
            throw new IllegalArgumentException("player and time are required");
        }
        expiresAtByPlayer.entrySet().removeIf(entry -> !entry.getValue().isAfter(now));
        var expiresAt = expiresAtByPlayer.get(playerUuid);
        if (expiresAt != null) {
            return new Attempt(false, Duration.between(now, expiresAt));
        }
        expiresAtByPlayer.put(playerUuid, now.plus(cooldown));
        return new Attempt(true, Duration.ZERO);
    }

    synchronized void release(UUID playerUuid) {
        expiresAtByPlayer.remove(playerUuid);
    }

    record Attempt(boolean allowed, Duration remaining) {
    }
}

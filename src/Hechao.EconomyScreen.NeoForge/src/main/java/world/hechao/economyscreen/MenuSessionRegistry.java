package world.hechao.economyscreen;

import java.time.Duration;
import java.time.Instant;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

final class MenuSessionRegistry {
    private final ConcurrentHashMap<UUID, Session> sessions = new ConcurrentHashMap<>();
    private final Duration lifetime;
    private final Duration minimumActionInterval;

    MenuSessionRegistry(Duration lifetime, Duration minimumActionInterval) {
        this.lifetime = lifetime;
        this.minimumActionInterval = minimumActionInterval;
    }

    UUID issue(UUID playerUuid, Instant now) {
        var sessionId = UUID.randomUUID();
        sessions.put(playerUuid, new Session(
                sessionId,
                now.plus(lifetime),
                Instant.EPOCH));
        return sessionId;
    }

    Validation validateAndConsume(
            UUID playerUuid,
            UUID sessionId,
            Instant now) {
        var result = new Validation[] { Validation.MISSING };
        sessions.compute(playerUuid, (ignored, current) -> {
            if (current == null || !current.sessionId.equals(sessionId)) {
                result[0] = Validation.MISSING;
                return current;
            }
            if (!current.expiresAt.isAfter(now)) {
                result[0] = Validation.EXPIRED;
                return null;
            }
            if (current.lastAction.plus(minimumActionInterval).isAfter(now)) {
                result[0] = Validation.RATE_LIMITED;
                return current;
            }
            result[0] = Validation.ALLOWED;
            return current.withLastAction(now);
        });
        return result[0];
    }

    enum Validation {
        ALLOWED,
        MISSING,
        EXPIRED,
        RATE_LIMITED
    }

    private record Session(UUID sessionId, Instant expiresAt, Instant lastAction) {
        Session withLastAction(Instant value) {
            return new Session(sessionId, expiresAt, value);
        }
    }
}

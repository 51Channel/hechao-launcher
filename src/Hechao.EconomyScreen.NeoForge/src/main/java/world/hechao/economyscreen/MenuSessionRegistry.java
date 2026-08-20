package world.hechao.economyscreen;

import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.Set;
import java.util.UUID;

final class MenuSessionRegistry {
    private final HashMap<UUID, Session> sessions = new HashMap<>();
    private final HashMap<UUID, Instant> lastActions = new HashMap<>();
    private final Duration lifetime;
    private final Duration minimumActionInterval;

    MenuSessionRegistry(Duration lifetime, Duration minimumActionInterval) {
        this.lifetime = lifetime;
        this.minimumActionInterval = minimumActionInterval;
    }

    synchronized UUID issue(UUID playerUuid, Set<String> allowedActions, Instant now) {
        cleanup(now);
        var sessionId = UUID.randomUUID();
        sessions.put(playerUuid, new Session(
                sessionId,
                now.plus(lifetime),
                Set.copyOf(allowedActions)));
        return sessionId;
    }

    synchronized Validation validateAndConsume(
            UUID playerUuid,
            UUID sessionId,
            String actionId,
            Instant now) {
        var current = sessions.get(playerUuid);
        if (current == null || !current.sessionId.equals(sessionId)) {
            return Validation.MISSING;
        }

        if (!current.expiresAt.isAfter(now)) {
            sessions.remove(playerUuid);
            return Validation.EXPIRED;
        }
        if (!current.allowedActions.contains(actionId)) {
            return Validation.ACTION_NOT_ALLOWED;
        }
        var previousAction = lastActions.get(playerUuid);
        if (previousAction != null
                && previousAction.plus(minimumActionInterval).isAfter(now)) {
            return Validation.RATE_LIMITED;
        }
        sessions.remove(playerUuid);
        lastActions.put(playerUuid, now);
        return Validation.ALLOWED;
    }

    synchronized void remove(UUID playerUuid) {
        sessions.remove(playerUuid);
        lastActions.remove(playerUuid);
    }

    synchronized int activeSessionCount() {
        return sessions.size();
    }

    private void cleanup(Instant now) {
        sessions.entrySet().removeIf(entry -> !entry.getValue().expiresAt.isAfter(now));
        var actionCutoff = now.minus(lifetime.multipliedBy(2));
        lastActions.entrySet().removeIf(entry -> entry.getValue().isBefore(actionCutoff));
    }

    enum Validation {
        ALLOWED,
        MISSING,
        EXPIRED,
        RATE_LIMITED,
        ACTION_NOT_ALLOWED
    }

    private record Session(
            UUID sessionId,
            Instant expiresAt,
            Set<String> allowedActions) {
    }
}

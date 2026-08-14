package world.hechao.economy;

import java.math.BigDecimal;
import java.time.Duration;
import java.time.Instant;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

final class BalanceCache {
    private final ConcurrentHashMap<UUID, Entry> values = new ConcurrentHashMap<>();
    private final Duration lifetime;

    BalanceCache(Duration lifetime) {
        this.lifetime = lifetime;
    }

    void put(UUID playerUuid, BigDecimal balance) {
        values.put(playerUuid, new Entry(balance, Instant.now()));
    }

    Optional<BigDecimal> getFresh(UUID playerUuid) {
        var value = values.get(playerUuid);
        return value != null && value.observedAt.plus(lifetime).isAfter(Instant.now())
                ? Optional.of(value.balance)
                : Optional.empty();
    }

    Optional<BigDecimal> getAny(UUID playerUuid) {
        var value = values.get(playerUuid);
        return value == null ? Optional.empty() : Optional.of(value.balance);
    }

    private record Entry(BigDecimal balance, Instant observedAt) {
    }
}

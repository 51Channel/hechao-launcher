package world.hechao.economy;

import java.math.BigDecimal;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executor;
import java.util.function.Supplier;

final class BalanceRefreshCoordinator {
    private final ConcurrentHashMap<UUID, CompletableFuture<BigDecimal>> inFlight =
            new ConcurrentHashMap<>();

    CompletableFuture<BigDecimal> refresh(
            UUID playerUuid,
            Executor executor,
            Supplier<BigDecimal> loader) {
        var created = new CompletableFuture<BigDecimal>();
        var existing = inFlight.putIfAbsent(playerUuid, created);
        if (existing != null) {
            return existing;
        }

        executor.execute(() -> {
            try {
                created.complete(loader.get());
            } catch (Throwable exception) {
                created.completeExceptionally(exception);
            } finally {
                inFlight.remove(playerUuid, created);
            }
        });
        return created;
    }

    int inFlightCount() {
        return inFlight.size();
    }

    void clear() {
        inFlight.clear();
    }
}

package world.hechao.economy;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;

import java.math.BigDecimal;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

final class BalanceRefreshCoordinatorTest {
    @Test
    void coalescesConcurrentRefreshesForTheSamePlayer() throws Exception {
        var coordinator = new BalanceRefreshCoordinator();
        var playerUuid = UUID.randomUUID();
        var entered = new CountDownLatch(1);
        var release = new CountDownLatch(1);
        var calls = new AtomicInteger();

        try (var executor = Executors.newVirtualThreadPerTaskExecutor()) {
            var first = coordinator.refresh(playerUuid, executor, () -> {
                calls.incrementAndGet();
                entered.countDown();
                try {
                    release.await();
                } catch (InterruptedException exception) {
                    Thread.currentThread().interrupt();
                    throw new IllegalStateException(exception);
                }
                return new BigDecimal("12.34");
            });
            entered.await(5, TimeUnit.SECONDS);
            var second = coordinator.refresh(
                    playerUuid,
                    executor,
                    () -> new BigDecimal("99.99"));

            assertSame(first, second);
            release.countDown();
            assertEquals(new BigDecimal("12.34"), first.get(5, TimeUnit.SECONDS));
            assertEquals(1, calls.get());
            assertEquals(0, coordinator.inFlightCount());
        }
    }
}

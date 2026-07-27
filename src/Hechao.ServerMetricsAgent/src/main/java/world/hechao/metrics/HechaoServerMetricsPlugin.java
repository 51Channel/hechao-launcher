package world.hechao.metrics;

import java.io.IOException;
import java.lang.management.ManagementFactory;
import java.nio.file.Path;
import java.time.Instant;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import org.bukkit.Bukkit;
import org.bukkit.plugin.java.JavaPlugin;
import org.bukkit.scheduler.BukkitTask;

public final class HechaoServerMetricsPlugin extends JavaPlugin {
    private static final long INITIAL_DELAY_TICKS = 20;
    private static final long SAMPLE_PERIOD_TICKS = 100;
    private static final long ERROR_LOG_INTERVAL_MILLISECONDS = 60_000;

    private final ServerMetricsFileWriter writer = new ServerMetricsFileWriter();
    private final AtomicReference<ServerMetricsSnapshot> pending =
            new AtomicReference<>();
    private final AtomicBoolean writerScheduled = new AtomicBoolean();
    private final AtomicLong lastErrorLogAt = new AtomicLong();

    private ExecutorService ioExecutor;
    private BukkitTask samplingTask;
    private Path metricsPath;

    @Override
    public void onEnable() {
        metricsPath = getDataFolder().toPath().resolve("metrics.json");
        ioExecutor = Executors.newSingleThreadExecutor(runnable -> {
            var thread = new Thread(runnable, "hechao-server-metrics-writer");
            thread.setDaemon(true);
            return thread;
        });
        samplingTask = Bukkit.getScheduler().runTaskTimer(
                this,
                this::captureAndQueue,
                INITIAL_DELAY_TICKS,
                SAMPLE_PERIOD_TICKS);
        getLogger().info(
                "Read-only TPS, MSPT and GC metrics will be written every 5 seconds.");
    }

    @Override
    public void onDisable() {
        if (samplingTask != null) {
            samplingTask.cancel();
        }
        if (ioExecutor != null) {
            ioExecutor.shutdown();
            try {
                if (!ioExecutor.awaitTermination(2, TimeUnit.SECONDS)) {
                    ioExecutor.shutdownNow();
                }
            } catch (InterruptedException exception) {
                ioExecutor.shutdownNow();
                Thread.currentThread().interrupt();
            }
        }
    }

    private void captureAndQueue() {
        var tps = Bukkit.getTPS();
        if (tps.length < 3) {
            logWriteFailure(new IllegalStateException(
                    "Paper returned fewer than three TPS windows"));
            return;
        }

        var snapshot = new ServerMetricsSnapshot(
                Instant.now(),
                clamp(tps[0], 0, 20.1),
                clamp(tps[1], 0, 20.1),
                clamp(tps[2], 0, 20.1),
                clamp(Bukkit.getAverageTickTime(), 0, 60_000),
                readGcCollectionTime());
        pending.set(snapshot);
        scheduleWriter();
    }

    private void scheduleWriter() {
        if (writerScheduled.compareAndSet(false, true)) {
            ioExecutor.execute(this::drainPendingWrites);
        }
    }

    private void drainPendingWrites() {
        try {
            ServerMetricsSnapshot snapshot;
            while ((snapshot = pending.getAndSet(null)) != null) {
                writer.write(metricsPath, snapshot);
            }
        } catch (IOException | RuntimeException exception) {
            logWriteFailure(exception);
        } finally {
            writerScheduled.set(false);
            if (pending.get() != null) {
                scheduleWriter();
            }
        }
    }

    private void logWriteFailure(Exception exception) {
        var now = System.currentTimeMillis();
        var previous = lastErrorLogAt.get();
        if (now - previous >= ERROR_LOG_INTERVAL_MILLISECONDS &&
                lastErrorLogAt.compareAndSet(previous, now)) {
            getLogger().warning(
                    "Unable to update the local metrics snapshot: " +
                            exception.getClass().getSimpleName());
        }
    }

    private static long readGcCollectionTime() {
        return ManagementFactory.getGarbageCollectorMXBeans()
                .stream()
                .mapToLong(bean -> Math.max(bean.getCollectionTime(), 0))
                .sum();
    }

    private static double clamp(double value, double minimum, double maximum) {
        if (!Double.isFinite(value)) {
            return minimum;
        }
        return Math.max(minimum, Math.min(maximum, value));
    }
}

package world.hechao.metricsmod;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Objects;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;

public final class ServerMetricsRuntime implements AutoCloseable {
    private static final long ERROR_LOG_INTERVAL_MILLISECONDS = 60_000;

    private final ServerMetricsFileWriter writer = new ServerMetricsFileWriter();
    private final AtomicReference<ServerMetricsSnapshot> pending =
            new AtomicReference<>();
    private final AtomicBoolean writerScheduled = new AtomicBoolean();
    private final AtomicBoolean closed = new AtomicBoolean();
    private final AtomicLong lastErrorLogAt = new AtomicLong();
    private final ExecutorService ioExecutor;
    private final Path metricsPath;
    private final Consumer<String> warningLogger;

    public ServerMetricsRuntime(
            Path metricsPath,
            Consumer<String> warningLogger) {
        this.metricsPath = Objects.requireNonNull(metricsPath, "metricsPath");
        this.warningLogger = Objects.requireNonNull(
                warningLogger,
                "warningLogger");
        ioExecutor = Executors.newSingleThreadExecutor(runnable -> {
            var thread = new Thread(
                    runnable,
                    "hechao-mod-server-metrics-writer");
            thread.setDaemon(true);
            return thread;
        });
    }

    public void publish(ServerMetricsSnapshot snapshot) {
        Objects.requireNonNull(snapshot, "snapshot");
        if (closed.get()) {
            return;
        }
        pending.set(snapshot);
        scheduleWriter();
    }

    @Override
    public void close() {
        if (!closed.compareAndSet(false, true)) {
            return;
        }

        try {
            ioExecutor.execute(this::drainPendingWrites);
        } catch (RejectedExecutionException exception) {
            logWriteFailure(exception);
        }
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

    private void scheduleWriter() {
        if (!writerScheduled.compareAndSet(false, true)) {
            return;
        }
        try {
            ioExecutor.execute(this::drainPendingWrites);
        } catch (RejectedExecutionException exception) {
            writerScheduled.set(false);
            if (!closed.get()) {
                logWriteFailure(exception);
            }
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
            if (pending.get() != null && !closed.get()) {
                scheduleWriter();
            }
        }
    }

    private void logWriteFailure(Exception exception) {
        var now = System.currentTimeMillis();
        var previous = lastErrorLogAt.get();
        if (now - previous >= ERROR_LOG_INTERVAL_MILLISECONDS &&
                lastErrorLogAt.compareAndSet(previous, now)) {
            warningLogger.accept(
                    "Unable to update the local metrics snapshot: " +
                            exception.getClass().getSimpleName());
        }
    }
}

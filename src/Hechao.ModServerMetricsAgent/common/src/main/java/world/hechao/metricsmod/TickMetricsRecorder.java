package world.hechao.metricsmod;

import java.lang.management.ManagementFactory;
import java.time.Instant;
import java.util.Optional;
import java.util.function.LongSupplier;

public final class TickMetricsRecorder {
    private static final int DEFAULT_PUBLISH_INTERVAL_TICKS = 100;
    private static final int MAXIMUM_TPS_SAMPLES = 18_001;
    private static final int MAXIMUM_MSPT_SAMPLES = 1_200;
    private static final long ONE_MINUTE_NANOS = 60_000_000_000L;
    private static final long FIVE_MINUTES_NANOS = 300_000_000_000L;
    private static final long FIFTEEN_MINUTES_NANOS = 900_000_000_000L;

    private final int publishIntervalTicks;
    private final LongSupplier gcCollectionTimeSupplier;
    private final long[] completionTimes = new long[MAXIMUM_TPS_SAMPLES];
    private final long[] tickDurations = new long[MAXIMUM_MSPT_SAMPLES];

    private int completionCursor;
    private int completionCount;
    private int durationCursor;
    private int durationCount;
    private long tickStartNanos = -1;
    private long completedTicks;

    public TickMetricsRecorder() {
        this(DEFAULT_PUBLISH_INTERVAL_TICKS, TickMetricsRecorder::readGcCollectionTime);
    }

    TickMetricsRecorder(
            int publishIntervalTicks,
            LongSupplier gcCollectionTimeSupplier) {
        if (publishIntervalTicks < 1) {
            throw new IllegalArgumentException(
                    "publishIntervalTicks must be positive");
        }
        this.publishIntervalTicks = publishIntervalTicks;
        this.gcCollectionTimeSupplier = gcCollectionTimeSupplier;
    }

    public void onTickStart(long startedAtNanos) {
        tickStartNanos = startedAtNanos;
    }

    public Optional<ServerMetricsSnapshot> onTickEnd(
            long completedAtNanos,
            Instant capturedAt) {
        if (tickStartNanos < 0 || completedAtNanos < tickStartNanos) {
            tickStartNanos = -1;
            return Optional.empty();
        }

        var duration = completedAtNanos - tickStartNanos;
        tickStartNanos = -1;
        recordCompletion(completedAtNanos);
        recordDuration(duration);
        completedTicks++;

        if (completedTicks % publishIntervalTicks != 0) {
            return Optional.empty();
        }

        return Optional.of(new ServerMetricsSnapshot(
                capturedAt,
                calculateTps(completedAtNanos, ONE_MINUTE_NANOS),
                calculateTps(completedAtNanos, FIVE_MINUTES_NANOS),
                calculateTps(completedAtNanos, FIFTEEN_MINUTES_NANOS),
                calculateAverageMspt(),
                Math.max(gcCollectionTimeSupplier.getAsLong(), 0)));
    }

    private void recordCompletion(long completedAtNanos) {
        completionTimes[completionCursor] = completedAtNanos;
        completionCursor = (completionCursor + 1) % completionTimes.length;
        completionCount = Math.min(completionCount + 1, completionTimes.length);
    }

    private void recordDuration(long durationNanos) {
        tickDurations[durationCursor] = durationNanos;
        durationCursor = (durationCursor + 1) % tickDurations.length;
        durationCount = Math.min(durationCount + 1, tickDurations.length);
    }

    private double calculateTps(long newestCompletion, long windowNanos) {
        if (completionCount < 2) {
            return 20;
        }

        var threshold = newestCompletion - windowNanos;
        var oldestCompletion = newestCompletion;
        var included = 1;

        for (var offset = 1; offset < completionCount; offset++) {
            var index = floorMod(
                    completionCursor - 1 - offset,
                    completionTimes.length);
            var candidate = completionTimes[index];
            if (candidate < threshold) {
                return clamp(
                        included * 1_000_000_000.0 / windowNanos,
                        0,
                        20);
            }
            oldestCompletion = candidate;
            included++;
        }

        var elapsedNanos = newestCompletion - oldestCompletion;
        if (included < 2 || elapsedNanos <= 0) {
            return 20;
        }

        var ticksPerSecond =
                (included - 1) * 1_000_000_000.0 / elapsedNanos;
        return clamp(ticksPerSecond, 0, 20);
    }

    private double calculateAverageMspt() {
        if (durationCount == 0) {
            return 0;
        }

        long totalNanos = 0;
        for (var index = 0; index < durationCount; index++) {
            totalNanos += tickDurations[index];
        }
        return clamp(
                totalNanos / (double) durationCount / 1_000_000.0,
                0,
                60_000);
    }

    private static int floorMod(int value, int divisor) {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
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

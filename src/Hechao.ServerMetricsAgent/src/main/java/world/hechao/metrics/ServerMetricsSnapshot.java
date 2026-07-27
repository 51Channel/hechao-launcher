package world.hechao.metrics;

import java.time.Instant;
import java.util.Objects;

record ServerMetricsSnapshot(
        Instant capturedAt,
        double tps1m,
        double tps5m,
        double tps15m,
        double msptAverage,
        long gcCollectionTimeMilliseconds) {

    ServerMetricsSnapshot {
        Objects.requireNonNull(capturedAt, "capturedAt");
        requireFiniteRange(tps1m, 0, 20.1, "tps1m");
        requireFiniteRange(tps5m, 0, 20.1, "tps5m");
        requireFiniteRange(tps15m, 0, 20.1, "tps15m");
        requireFiniteRange(msptAverage, 0, 60_000, "msptAverage");
        if (gcCollectionTimeMilliseconds < 0) {
            throw new IllegalArgumentException(
                    "gcCollectionTimeMilliseconds must not be negative");
        }
    }

    private static void requireFiniteRange(
            double value,
            double minimum,
            double maximum,
            String name) {
        if (!Double.isFinite(value) || value < minimum || value > maximum) {
            throw new IllegalArgumentException(name + " is outside the allowed range");
        }
    }
}

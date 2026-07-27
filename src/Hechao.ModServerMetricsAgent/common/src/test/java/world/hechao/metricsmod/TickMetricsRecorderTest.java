package world.hechao.metricsmod;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Instant;
import org.junit.jupiter.api.Test;

final class TickMetricsRecorderTest {
    @Test
    void publishesTwentyTpsAndMeasuredTickTime() {
        var recorder = new TickMetricsRecorder(100, () -> 321);
        var snapshot = simulate(recorder, 100, 50_000_000L, 10_000_000L);

        assertEquals(20, snapshot.tps1m(), 0.01);
        assertEquals(20, snapshot.tps5m(), 0.01);
        assertEquals(20, snapshot.tps15m(), 0.01);
        assertEquals(10, snapshot.msptAverage(), 0.01);
        assertEquals(321, snapshot.gcCollectionTimeMilliseconds());
    }

    @Test
    void reportsReducedTpsWithoutConfusingItWithMspt() {
        var recorder = new TickMetricsRecorder(100, () -> 0);
        var snapshot = simulate(recorder, 100, 100_000_000L, 70_000_000L);

        assertEquals(10, snapshot.tps1m(), 0.01);
        assertEquals(70, snapshot.msptAverage(), 0.01);
    }

    @Test
    void doesNotReportTwentyTpsAfterAWindowLengthStall() {
        var recorder = new TickMetricsRecorder(2, () -> 0);
        recorder.onTickStart(0);
        assertTrue(recorder.onTickEnd(
                10_000_000L,
                Instant.EPOCH).isEmpty());

        recorder.onTickStart(120_000_000_000L);
        var snapshot = recorder.onTickEnd(
                        120_010_000_000L,
                        Instant.EPOCH.plusSeconds(120))
                .orElseThrow();

        assertEquals(1.0 / 60.0, snapshot.tps1m(), 0.001);
        assertEquals(1.0 / 120.0, snapshot.tps5m(), 0.001);
        assertEquals(1.0 / 120.0, snapshot.tps15m(), 0.001);
    }

    @Test
    void ignoresAnEndEventWithoutAValidStart() {
        var recorder = new TickMetricsRecorder(1, () -> 0);

        assertTrue(recorder.onTickEnd(10, Instant.EPOCH).isEmpty());
        recorder.onTickStart(20);
        assertTrue(recorder.onTickEnd(19, Instant.EPOCH).isEmpty());
    }

    private static ServerMetricsSnapshot simulate(
            TickMetricsRecorder recorder,
            int ticks,
            long intervalNanos,
            long durationNanos) {
        ServerMetricsSnapshot snapshot = null;
        long startedAt = 0;
        for (var tick = 0; tick < ticks; tick++) {
            recorder.onTickStart(startedAt);
            snapshot = recorder.onTickEnd(
                            startedAt + durationNanos,
                            Instant.EPOCH.plusNanos(startedAt + durationNanos))
                    .orElse(snapshot);
            startedAt += intervalNanos;
        }
        return snapshot;
    }
}

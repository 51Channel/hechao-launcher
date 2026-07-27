package world.hechao.metrics;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.google.gson.JsonParser;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.time.Instant;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

final class ServerMetricsFileWriterTest {
    @TempDir
    java.nio.file.Path directory;

    @Test
    void writeCreatesCompleteAtomicSnapshot() throws Exception {
        var capturedAt = Instant.parse("2026-07-27T08:30:00Z");
        var snapshot = new ServerMetricsSnapshot(
                capturedAt,
                19.98,
                19.97,
                19.96,
                18.4,
                12_345);
        var destination = directory.resolve("nested").resolve("metrics.json");

        new ServerMetricsFileWriter().write(destination, snapshot);

        var root = JsonParser.parseString(
                Files.readString(destination, StandardCharsets.UTF_8))
                .getAsJsonObject();
        assertEquals(1, root.get("schemaVersion").getAsInt());
        assertEquals(capturedAt.toString(), root.get("capturedAt").getAsString());
        assertEquals(19.98, root.get("tps1m").getAsDouble());
        assertEquals(18.4, root.get("msptAverage").getAsDouble());
        assertEquals(
                12_345,
                root.get("gcCollectionTimeMilliseconds").getAsLong());
        assertFalse(Files.exists(
                destination.getParent().resolve("metrics.json.tmp")));
    }

    @Test
    void snapshotRejectsInvalidValues() {
        assertThrows(
                IllegalArgumentException.class,
                () -> new ServerMetricsSnapshot(
                        Instant.now(),
                        Double.NaN,
                        20,
                        20,
                        10,
                        0));
        assertThrows(
                IllegalArgumentException.class,
                () -> new ServerMetricsSnapshot(
                        Instant.now(),
                        20,
                        20,
                        20,
                        10,
                        -1));
    }
}

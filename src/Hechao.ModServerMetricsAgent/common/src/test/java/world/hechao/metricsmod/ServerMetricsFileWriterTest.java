package world.hechao.metricsmod;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.time.Instant;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

final class ServerMetricsFileWriterTest {
    @TempDir
    java.nio.file.Path temporaryDirectory;

    @Test
    void writesTheCollectorSchemaAndReplacesTheTemporaryFile() throws Exception {
        var destination = temporaryDirectory
                .resolve("plugins")
                .resolve("HechaoServerMetrics")
                .resolve("metrics.json");
        var snapshot = new ServerMetricsSnapshot(
                Instant.parse("2026-07-28T00:00:00Z"),
                20,
                19.5,
                18.75,
                12.5,
                1234);

        new ServerMetricsFileWriter().write(destination, snapshot);

        var json = Files.readString(destination, StandardCharsets.UTF_8);
        assertTrue(json.contains("\"schemaVersion\":1"));
        assertTrue(json.contains("\"capturedAt\":\"2026-07-28T00:00:00Z\""));
        assertTrue(json.contains("\"tps5m\":19.5"));
        assertTrue(json.contains("\"msptAverage\":12.5"));
        assertTrue(json.contains("\"gcCollectionTimeMilliseconds\":1234"));
        assertFalse(Files.exists(destination.resolveSibling("metrics.json.tmp")));
    }
}

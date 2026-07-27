package world.hechao.metricsmod;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.time.Instant;
import java.util.ArrayList;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

final class ServerMetricsRuntimeTest {
    @TempDir
    java.nio.file.Path temporaryDirectory;

    @Test
    void closeFlushesTheNewestPendingSnapshotOnTheWriterThread()
            throws Exception {
        var warnings = new ArrayList<String>();
        var destination = temporaryDirectory.resolve("metrics.json");
        var runtime = new ServerMetricsRuntime(destination, warnings::add);

        for (var index = 1; index <= 100; index++) {
            runtime.publish(new ServerMetricsSnapshot(
                    Instant.parse("2026-07-28T00:00:00Z").plusSeconds(index),
                    20,
                    20,
                    20,
                    index,
                    index));
        }
        runtime.close();

        var json = Files.readString(destination, StandardCharsets.UTF_8);
        assertTrue(json.contains("\"msptAverage\":100.0"));
        assertTrue(json.contains("\"gcCollectionTimeMilliseconds\":100"));
        assertTrue(warnings.isEmpty());
    }
}

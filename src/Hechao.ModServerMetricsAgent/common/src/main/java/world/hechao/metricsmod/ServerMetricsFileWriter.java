package world.hechao.metricsmod;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.StandardOpenOption;

final class ServerMetricsFileWriter {
    void write(Path destination, ServerMetricsSnapshot snapshot) throws IOException {
        var normalized = destination.toAbsolutePath().normalize();
        var parent = normalized.getParent();
        if (parent == null) {
            throw new IOException("The metrics destination has no parent directory");
        }

        Files.createDirectories(parent);
        var temporary = parent.resolve(normalized.getFileName() + ".tmp");
        Files.writeString(
                temporary,
                toJson(snapshot),
                StandardCharsets.UTF_8,
                StandardOpenOption.CREATE,
                StandardOpenOption.TRUNCATE_EXISTING,
                StandardOpenOption.WRITE);
        try {
            Files.move(
                    temporary,
                    normalized,
                    StandardCopyOption.ATOMIC_MOVE,
                    StandardCopyOption.REPLACE_EXISTING);
        } catch (AtomicMoveNotSupportedException exception) {
            Files.move(
                    temporary,
                    normalized,
                    StandardCopyOption.REPLACE_EXISTING);
        }
    }

    private static String toJson(ServerMetricsSnapshot snapshot) {
        return new StringBuilder(256)
                .append('{')
                .append("\"schemaVersion\":1,")
                .append("\"capturedAt\":\"")
                .append(snapshot.capturedAt())
                .append("\",")
                .append("\"tps1m\":")
                .append(snapshot.tps1m())
                .append(',')
                .append("\"tps5m\":")
                .append(snapshot.tps5m())
                .append(',')
                .append("\"tps15m\":")
                .append(snapshot.tps15m())
                .append(',')
                .append("\"msptAverage\":")
                .append(snapshot.msptAverage())
                .append(',')
                .append("\"gcCollectionTimeMilliseconds\":")
                .append(snapshot.gcCollectionTimeMilliseconds())
                .append('}')
                .toString();
    }
}

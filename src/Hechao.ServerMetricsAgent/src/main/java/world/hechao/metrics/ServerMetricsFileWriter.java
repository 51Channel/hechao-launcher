package world.hechao.metrics;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonObject;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.StandardOpenOption;

final class ServerMetricsFileWriter {
    private static final Gson GSON = new GsonBuilder()
            .disableHtmlEscaping()
            .create();

    void write(Path destination, ServerMetricsSnapshot snapshot) throws IOException {
        var parent = destination.toAbsolutePath().normalize().getParent();
        if (parent == null) {
            throw new IOException("The metrics destination has no parent directory");
        }

        Files.createDirectories(parent);
        var temporary = parent.resolve(destination.getFileName() + ".tmp");
        var json = toJson(snapshot);
        Files.writeString(
                temporary,
                json,
                StandardCharsets.UTF_8,
                StandardOpenOption.CREATE,
                StandardOpenOption.TRUNCATE_EXISTING,
                StandardOpenOption.WRITE);
        try {
            Files.move(
                    temporary,
                    destination,
                    StandardCopyOption.ATOMIC_MOVE,
                    StandardCopyOption.REPLACE_EXISTING);
        } catch (AtomicMoveNotSupportedException exception) {
            Files.move(
                    temporary,
                    destination,
                    StandardCopyOption.REPLACE_EXISTING);
        }
    }

    private static String toJson(ServerMetricsSnapshot snapshot) {
        var root = new JsonObject();
        root.addProperty("schemaVersion", 1);
        root.addProperty("capturedAt", snapshot.capturedAt().toString());
        root.addProperty("tps1m", snapshot.tps1m());
        root.addProperty("tps5m", snapshot.tps5m());
        root.addProperty("tps15m", snapshot.tps15m());
        root.addProperty("msptAverage", snapshot.msptAverage());
        root.addProperty(
                "gcCollectionTimeMilliseconds",
                snapshot.gcCollectionTimeMilliseconds());
        return GSON.toJson(root);
    }
}

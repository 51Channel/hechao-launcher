package world.hechao.economy.inventory;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.UUID;
import java.util.logging.Logger;
import org.bukkit.configuration.file.YamlConfiguration;

public final class QuarantinedSaleStore {
    private final Path path;
    private final Logger logger;

    public QuarantinedSaleStore(Path path, Logger logger) {
        this.path = path;
        this.logger = logger;
    }

    public synchronized void add(
            UUID playerUuid,
            UUID operationId,
            String itemId,
            int quantity,
            String reason) {
        var yaml = YamlConfiguration.loadConfiguration(path.toFile());
        var key = "sales." + operationId;
        yaml.set(key + ".player-uuid", playerUuid.toString());
        yaml.set(key + ".item-id", itemId);
        yaml.set(key + ".quantity", quantity);
        yaml.set(key + ".reason", reason);
        yaml.set(key + ".created-at", Instant.now().toString());
        try {
            Files.createDirectories(path.getParent());
            yaml.save(path.toFile());
        } catch (IOException exception) {
            logger.severe(
                    "Unable to persist quarantined sale " + operationId
                            + "; keep the server stopped and recover from logs/backups.");
            logger.log(java.util.logging.Level.SEVERE, "Quarantine write failed", exception);
        }
    }

    public synchronized int count() {
        var yaml = YamlConfiguration.loadConfiguration(path.toFile());
        var section = yaml.getConfigurationSection("sales");
        return section == null ? 0 : section.getKeys(false).size();
    }
}

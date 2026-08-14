package world.hechao.tieragent;

import java.io.IOException;
import java.nio.file.Files;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.logging.Level;
import net.luckperms.api.LuckPermsProvider;
import org.bukkit.plugin.java.JavaPlugin;

public final class HechaoLuckPermsTierAgent extends JavaPlugin {
    private ScheduledExecutorService executor;

    @Override
    public void onEnable() {
        try {
            Files.createDirectories(getDataFolder().toPath());
            var configurationPath =
                    getDataFolder().toPath().resolve("config.properties");
            if (Files.notExists(configurationPath)) {
                saveResource("config.properties", false);
            }

            var configuration = TierAgentConfiguration.load(configurationPath);
            var gateway = new TierCommandApiClient(configuration);
            var mutationService = new LuckPermsTierMutationService(
                    LuckPermsProvider.get());
            var processor = new TierCommandProcessor(
                    gateway,
                    mutationService,
                    45,
                    message -> getLogger().warning(message));

            executor = Executors.newSingleThreadScheduledExecutor(task -> {
                var thread = new Thread(
                        task,
                        "hechao-luckperms-tier-agent");
                thread.setDaemon(true);
                return thread;
            });
            executor.scheduleWithFixedDelay(
                    processor::runOnce,
                    0,
                    configuration.pollInterval().toSeconds(),
                    TimeUnit.SECONDS);
            getLogger().info(
                    "LuckPerms tier agent enabled as "
                            + configuration.agentId()
                            + " (version "
                            + TierCommandApiClient.AGENT_VERSION
                            + ", protocol "
                            + TierCommandApiClient.PROTOCOL_VERSION
                            + ")");
        } catch (IOException | IllegalArgumentException exception) {
            getLogger().log(
                    Level.SEVERE,
                    "LuckPerms tier agent configuration is invalid.",
                    exception);
            getServer().getPluginManager().disablePlugin(this);
        } catch (IllegalStateException exception) {
            getLogger().log(
                    Level.SEVERE,
                    "LuckPerms API is not available.",
                    exception);
            getServer().getPluginManager().disablePlugin(this);
        }
    }

    @Override
    public void onDisable() {
        if (executor != null) {
            executor.shutdownNow();
            executor = null;
        }
    }
}

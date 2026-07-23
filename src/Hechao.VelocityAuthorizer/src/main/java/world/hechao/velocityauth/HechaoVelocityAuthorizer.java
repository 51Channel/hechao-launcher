package world.hechao.velocityauth;

import com.google.inject.Inject;
import com.velocitypowered.api.event.EventTask;
import com.velocitypowered.api.event.Subscribe;
import com.velocitypowered.api.event.connection.DisconnectEvent;
import com.velocitypowered.api.event.player.ServerPreConnectEvent;
import com.velocitypowered.api.event.proxy.ProxyInitializeEvent;
import com.velocitypowered.api.plugin.Plugin;
import com.velocitypowered.api.plugin.annotation.DataDirectory;
import com.velocitypowered.api.proxy.Player;
import java.net.InetAddress;
import java.nio.file.Path;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.slf4j.Logger;

@Plugin(
        id = "hechao-velocity-authorizer",
        name = "Hechao Velocity Authorizer",
        version = "0.1.0",
        description = "Server-side Microsoft UUID and LuckPerms authorization for Hechao",
        authors = {"Hechao"})
public final class HechaoVelocityAuthorizer {
    private static final String SERVICE_UNAVAILABLE_MESSAGE =
            "赫朝进服授权服务暂时不可用，请稍后重试。";

    private final Logger logger;
    private final Path dataDirectory;
    private final Set<UUID> authorizedConnections = ConcurrentHashMap.newKeySet();
    private volatile PluginConfiguration configuration;
    private volatile AuthorizationApiClient apiClient;

    @Inject
    public HechaoVelocityAuthorizer(
            Logger logger,
            @DataDirectory Path dataDirectory) {
        this.logger = logger;
        this.dataDirectory = dataDirectory;
    }

    @Subscribe
    public void onProxyInitialize(ProxyInitializeEvent event) {
        try {
            configuration = PluginConfiguration.load(dataDirectory);
            if (configuration.mode() == AuthorizationMode.DISABLED) {
                logger.info("Hechao authorization is disabled.");
                return;
            }

            if (configuration.hasCredential()) {
                apiClient = new AuthorizationApiClient(configuration);
                logger.info(
                        "Hechao authorization initialized in {} mode for proxy {}.",
                        configuration.mode().name().toLowerCase(),
                        configuration.proxyInstance());
            } else {
                logger.warn(
                        "Hechao authorization token is missing. Mode {} will use its failure behavior.",
                        configuration.mode().name().toLowerCase());
            }
        } catch (Exception exception) {
            AuthorizationMode failureMode = PluginConfiguration.readModeHint(dataDirectory);
            configuration = new PluginConfiguration(
                    failureMode,
                    java.net.URI.create(
                            "https://launcher-api.hechao.world/v1/internal/velocity/authorize"),
                    "",
                    "invalid-configuration",
                    java.time.Duration.ofMillis(2500));
            logger.error("Unable to load Hechao authorization configuration.", exception);
            if (failureMode == AuthorizationMode.ENFORCE) {
                logger.error("Enforce mode remains fail-closed until the configuration is fixed.");
            }
        }
    }

    @Subscribe(priority = 100)
    public EventTask onServerPreConnect(ServerPreConnectEvent event) {
        PluginConfiguration currentConfiguration = configuration;
        if (currentConfiguration == null
                || currentConfiguration.mode() == AuthorizationMode.DISABLED
                || !event.getResult().isAllowed()) {
            return null;
        }

        Player player = event.getPlayer();
        UUID playerId = player.getUniqueId();
        boolean initialConnection = !authorizedConnections.contains(playerId);
        String target = event.getOriginalServer().getServerInfo().getName().toLowerCase();
        String remoteAddress = getRemoteAddress(player);
        AuthorizationApiClient client = apiClient;

        return EventTask.withContinuation(continuation -> {
            CompletableFuture<AuthorizationDecision> authorization = client == null
                    ? CompletableFuture.failedFuture(
                            new IllegalStateException("Authorization API client is not configured"))
                    : client.authorize(
                            playerId,
                            player.getUsername(),
                            target,
                            initialConnection,
                            remoteAddress);

            authorization.whenComplete((decision, failure) -> {
                try {
                    applyDecision(
                            event,
                            currentConfiguration.mode(),
                            initialConnection,
                            target,
                            decision,
                            failure);
                } finally {
                    continuation.resume();
                }
            });
        });
    }

    @Subscribe
    public void onDisconnect(DisconnectEvent event) {
        authorizedConnections.remove(event.getPlayer().getUniqueId());
    }

    private void applyDecision(
            ServerPreConnectEvent event,
            AuthorizationMode mode,
            boolean initialConnection,
            String target,
            AuthorizationDecision decision,
            Throwable failure) {
        Player player = event.getPlayer();
        if (failure != null) {
            if (mode == AuthorizationMode.ENFORCE) {
                deny(event, initialConnection, SERVICE_UNAVAILABLE_MESSAGE);
                logger.warn(
                        "Denied {} -> {} because the authorization API was unavailable: {}",
                        player.getUsername(),
                        target,
                        rootMessage(failure));
            } else {
                authorizedConnections.add(player.getUniqueId());
                logger.warn(
                        "[monitor] Authorization API unavailable for {} -> {}: {}",
                        player.getUsername(),
                        target,
                        rootMessage(failure));
            }
            return;
        }

        if (decision.allowed()) {
            if (initialConnection) {
                authorizedConnections.add(player.getUniqueId());
            }
            return;
        }

        if (mode == AuthorizationMode.ENFORCE) {
            deny(event, initialConnection, safeMessage(decision.message()));
            logger.info(
                    "Denied {} -> {} with reason {}.",
                    player.getUsername(),
                    target,
                    decision.reason());
        } else {
            if (initialConnection) {
                authorizedConnections.add(player.getUniqueId());
            }
            logger.warn(
                    "[monitor] Would deny {} -> {} with reason {}.",
                    player.getUsername(),
                    target,
                    decision.reason());
        }
    }

    private static void deny(
            ServerPreConnectEvent event,
            boolean initialConnection,
            String message) {
        event.setResult(ServerPreConnectEvent.ServerResult.denied());
        Component component = Component.text(message, NamedTextColor.RED);
        if (initialConnection) {
            event.getPlayer().disconnect(component);
        } else {
            event.getPlayer().sendMessage(component);
        }
    }

    private static String getRemoteAddress(Player player) {
        InetAddress address = player.getRemoteAddress().getAddress();
        return address == null ? null : address.getHostAddress();
    }

    private static String safeMessage(String message) {
        if (message == null || message.isBlank() || message.length() > 240) {
            return "你暂时无法进入该服务器。";
        }
        return message;
    }

    private static String rootMessage(Throwable throwable) {
        Throwable current = throwable;
        while (current.getCause() != null) {
            current = current.getCause();
        }
        String message = current.getMessage();
        return message == null || message.isBlank()
                ? current.getClass().getSimpleName()
                : message;
    }
}

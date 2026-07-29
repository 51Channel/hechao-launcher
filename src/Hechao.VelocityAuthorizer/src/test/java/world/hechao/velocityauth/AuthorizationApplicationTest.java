package world.hechao.velocityauth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import com.velocitypowered.api.event.player.ServerPreConnectEvent;
import com.velocitypowered.api.proxy.Player;
import com.velocitypowered.api.proxy.ProxyServer;
import com.velocitypowered.api.proxy.server.RegisteredServer;
import com.velocitypowered.api.proxy.server.ServerInfo;
import java.nio.file.Path;
import java.util.UUID;
import net.kyori.adventure.text.Component;
import org.junit.jupiter.api.Test;
import org.slf4j.Logger;

final class AuthorizationApplicationTest {
    @Test
    void internalLobbyIsClosedEvenBeforePluginInitialization() {
        UUID playerId = UUID.randomUUID();
        Player player = mock(Player.class);
        when(player.getUniqueId()).thenReturn(playerId);
        when(player.getUsername()).thenReturn("Player");
        RegisteredServer lobby = mock(RegisteredServer.class);
        when(lobby.getServerInfo()).thenReturn(
                new ServerInfo("lobby", new java.net.InetSocketAddress("127.0.0.1", 25566)));
        ServerPreConnectEvent.ServerResult originalResult =
                mock(ServerPreConnectEvent.ServerResult.class);
        when(originalResult.isAllowed()).thenReturn(true);
        ServerPreConnectEvent event = mock(ServerPreConnectEvent.class);
        when(event.getPlayer()).thenReturn(player);
        when(event.getOriginalServer()).thenReturn(lobby);
        when(event.getResult()).thenReturn(originalResult);

        assertNull(plugin().onServerPreConnect(event));

        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(player).disconnect(any(Component.class));
    }

    @Test
    void playerTargetIsClosedBeforePluginInitialization() {
        UUID playerId = UUID.randomUUID();
        Player player = mock(Player.class);
        when(player.getUniqueId()).thenReturn(playerId);
        when(player.getUsername()).thenReturn("Player");
        RegisteredServer pvp = mock(RegisteredServer.class);
        when(pvp.getServerInfo()).thenReturn(
                new ServerInfo("pvp", new java.net.InetSocketAddress("127.0.0.1", 25567)));
        ServerPreConnectEvent.ServerResult originalResult =
                mock(ServerPreConnectEvent.ServerResult.class);
        when(originalResult.isAllowed()).thenReturn(true);
        ServerPreConnectEvent event = mock(ServerPreConnectEvent.class);
        when(event.getPlayer()).thenReturn(player);
        when(event.getOriginalServer()).thenReturn(pvp);
        when(event.getResult()).thenReturn(originalResult);

        assertNull(plugin().onServerPreConnect(event));

        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(player).disconnect(any(Component.class));
    }

    @Test
    void tracksAllowedTransferAsTheNextSessionSource() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();
        AuthorizationDecision decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "survival2",
                "survival2");

        plugin.applyDecision(
                event,
                AuthorizationMode.MONITOR,
                false,
                "survival2",
                decision,
                null);

        assertEquals("survival2", plugin.authorizedServer(playerId));
        verify(event, never()).setResult(any());
    }

    @Test
    void monitorModeFailsClosedForInitialRouteWhenApiIsUnavailable() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();

        plugin.applyDecision(
                event,
                AuthorizationMode.MONITOR,
                true,
                "pvp",
                null,
                new IllegalStateException("synthetic outage"));

        assertNull(plugin.authorizedServer(playerId));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void enforceModeFailsClosedWhenApiIsUnavailable() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();

        plugin.applyDecision(
                event,
                AuthorizationMode.ENFORCE,
                true,
                "pvp",
                null,
                new IllegalStateException("synthetic outage"));

        assertNull(plugin.authorizedServer(playerId));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void enforceModeRejectsAllowedTransferWithoutSessionServerId() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();
        AuthorizationDecision decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                null,
                "survival2");

        plugin.applyDecision(
                event,
                AuthorizationMode.ENFORCE,
                false,
                "survival2",
                decision,
                null);

        assertNull(plugin.authorizedServer(playerId));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).sendMessage(any(Component.class));
        verify(event.getPlayer(), never()).disconnect(any(Component.class));
    }

    @Test
    void monitorModeTracksAllowedTransferWithoutSessionServerId() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();
        AuthorizationDecision decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                null,
                "survival2");

        plugin.applyDecision(
                event,
                AuthorizationMode.MONITOR,
                false,
                "survival2",
                decision,
                null);

        assertEquals("survival2", plugin.authorizedServer(playerId));
        verify(event, never()).setResult(any());
    }

    @Test
    void monitorModeTracksFailOpenPolicyTransfer() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();
        AuthorizationDecision decision = new AuthorizationDecision(
                false,
                "InsufficientTier",
                "denied",
                "survival2",
                "survival2");

        plugin.applyDecision(
                event,
                AuthorizationMode.MONITOR,
                false,
                "survival2",
                decision,
                null);

        assertEquals("survival2", plugin.authorizedServer(playerId));
        verify(event, never()).setResult(any());
    }

    @Test
    void monitorModeFailsClosedForDeniedInitialConnection() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();
        AuthorizationDecision decision = new AuthorizationDecision(
                false,
                "InsufficientTier",
                "denied",
                "pvp",
                "pvp");

        plugin.applyDecision(
                event,
                AuthorizationMode.MONITOR,
                true,
                "lobby",
                decision,
                null);

        assertNull(plugin.authorizedServer(playerId));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void allowedInternalTargetIsAlwaysDenied() {
        UUID playerId = UUID.randomUUID();
        ServerPreConnectEvent event = eventFor(playerId);
        HechaoVelocityAuthorizer plugin = plugin();
        AuthorizationDecision decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "lobby",
                "lobby");

        plugin.applyDecision(
                event,
                AuthorizationMode.MONITOR,
                false,
                "lobby",
                decision,
                null);

        assertNull(plugin.authorizedServer(playerId));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).sendMessage(any(Component.class));
    }

    private static HechaoVelocityAuthorizer plugin() {
        return new HechaoVelocityAuthorizer(
                mock(Logger.class),
                Path.of("."),
                mock(ProxyServer.class));
    }

    private static ServerPreConnectEvent eventFor(UUID playerId) {
        Player player = mock(Player.class);
        when(player.getUniqueId()).thenReturn(playerId);
        when(player.getUsername()).thenReturn("Player");
        ServerPreConnectEvent event = mock(ServerPreConnectEvent.class);
        when(event.getPlayer()).thenReturn(player);
        return event;
    }
}

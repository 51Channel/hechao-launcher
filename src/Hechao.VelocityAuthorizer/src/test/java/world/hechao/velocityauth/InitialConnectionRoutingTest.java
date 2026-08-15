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
import java.net.InetSocketAddress;
import java.nio.file.Path;
import java.util.Optional;
import net.kyori.adventure.text.Component;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.slf4j.Logger;

final class InitialConnectionRoutingTest {
    @Test
    void routesAllowedInitialConnectionToGrantedTarget() {
        ProxyServer proxy = mock(ProxyServer.class);
        RegisteredServer destination = mock(RegisteredServer.class);
        when(proxy.getServer("pvp")).thenReturn(Optional.of(destination));

        ServerPreConnectEvent event = eventFor("Player");
        var plugin = plugin(proxy);
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "pvp",
                "PVP");

        assertEquals("pvp", plugin.routeInitialConnection(
                event,
                "lobby",
                decision));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
    }

    @Test
    void leavesPlayerTargetWhenGrantAlreadyMatches() {
        ProxyServer proxy = mock(ProxyServer.class);
        ServerPreConnectEvent event = eventFor("Player");
        var plugin = plugin(proxy);
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "pvp",
                "pvp");

        assertEquals("pvp", plugin.routeInitialConnection(
                event,
                "pvp",
                decision));
        verify(proxy, never()).getServer(any());
        verify(event, never()).setResult(any());
    }

    @Test
    void enforceModeFailsClosedWhenGrantedTargetIsMissing() {
        ProxyServer proxy = mock(ProxyServer.class);
        when(proxy.getServer("missing")).thenReturn(Optional.empty());

        ServerPreConnectEvent event = eventFor("Player");
        var plugin = plugin(proxy);
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "missing",
                "missing");

        assertNull(plugin.routeInitialConnection(
                event,
                "lobby",
                decision));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void monitorModeFailsClosedWhenGrantedTargetIsMissing() {
        ProxyServer proxy = mock(ProxyServer.class);
        when(proxy.getServer("missing")).thenReturn(Optional.empty());

        ServerPreConnectEvent event = eventFor("Player");
        var plugin = plugin(proxy);
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "missing",
                "missing");

        assertNull(plugin.routeInitialConnection(
                event,
                "lobby",
                decision));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void rejectsInitialGrantToInternalLobby() {
        ProxyServer proxy = mock(ProxyServer.class);
        ServerPreConnectEvent event = eventFor("Player");
        var plugin = plugin(proxy);
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "lobby",
                "lobby");

        assertNull(plugin.routeInitialConnection(
                event,
                "lobby",
                decision));
        verify(proxy, never()).getServer(any());
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void registersApprovedDynamicBackendBeforeRouting() {
        ProxyServer proxy = mock(ProxyServer.class);
        RegisteredServer destination = mock(RegisteredServer.class);
        when(proxy.getServer("survival-industry")).thenReturn(Optional.empty());
        when(proxy.registerServer(any(ServerInfo.class))).thenReturn(destination);
        ServerPreConnectEvent event = eventFor("Player");
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "survival-industry",
                "survival-industry",
                "127.0.0.1",
                25600);

        assertEquals("survival-industry", plugin(proxy).routeInitialConnection(
                event,
                "lobby",
                decision));

        ArgumentCaptor<ServerInfo> serverInfo = ArgumentCaptor.forClass(ServerInfo.class);
        verify(proxy).registerServer(serverInfo.capture());
        assertEquals("survival-industry", serverInfo.getValue().getName());
        assertEquals(
                new InetSocketAddress("127.0.0.1", 25600),
                serverInfo.getValue().getAddress());
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
    }

    @Test
    void rejectsNonLoopbackDynamicBackend() {
        ProxyServer proxy = mock(ProxyServer.class);
        ServerPreConnectEvent event = eventFor("Player");
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "pvp-ranked",
                "pvp-ranked",
                "203.0.113.10",
                25600);

        assertNull(plugin(proxy).routeInitialConnection(event, "lobby", decision));
        verify(proxy, never()).registerServer(any(ServerInfo.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void rejectsConcurrentDynamicRegistrationAtDifferentAddress() {
        ProxyServer proxy = mock(ProxyServer.class);
        RegisteredServer raced = mock(RegisteredServer.class);
        when(raced.getServerInfo()).thenReturn(new ServerInfo(
                "survival-industry",
                new InetSocketAddress("127.0.0.1", 25601)));
        when(proxy.getServer("survival-industry"))
                .thenReturn(Optional.empty(), Optional.of(raced));
        when(proxy.registerServer(any(ServerInfo.class)))
                .thenThrow(new IllegalArgumentException("already registered"));
        ServerPreConnectEvent event = eventFor("Player");
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "survival-industry",
                "survival-industry",
                "127.0.0.1",
                25600);

        assertNull(plugin(proxy).routeInitialConnection(event, "lobby", decision));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    private static HechaoVelocityAuthorizer plugin(ProxyServer proxy) {
        return new HechaoVelocityAuthorizer(
                mock(Logger.class),
                Path.of("."),
                proxy);
    }

    private static ServerPreConnectEvent eventFor(String username) {
        Player player = mock(Player.class);
        when(player.getUsername()).thenReturn(username);
        ServerPreConnectEvent event = mock(ServerPreConnectEvent.class);
        when(event.getPlayer()).thenReturn(player);
        return event;
    }
}

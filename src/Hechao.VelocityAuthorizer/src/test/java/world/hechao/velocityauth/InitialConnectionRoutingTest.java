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
import java.nio.file.Path;
import java.util.Optional;
import net.kyori.adventure.text.Component;
import org.junit.jupiter.api.Test;
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
                AuthorizationMode.MONITOR,
                "lobby",
                decision));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
    }

    @Test
    void leavesOriginalTargetWhenGrantAlreadyMatches() {
        ProxyServer proxy = mock(ProxyServer.class);
        ServerPreConnectEvent event = eventFor("Player");
        var plugin = plugin(proxy);
        var decision = new AuthorizationDecision(
                true,
                "Allowed",
                "ok",
                "lobby",
                "lobby");

        assertEquals("lobby", plugin.routeInitialConnection(
                event,
                AuthorizationMode.MONITOR,
                "lobby",
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
                AuthorizationMode.ENFORCE,
                "lobby",
                decision));
        verify(event).setResult(any(ServerPreConnectEvent.ServerResult.class));
        verify(event.getPlayer()).disconnect(any(Component.class));
    }

    @Test
    void monitorModeTracksOriginalTargetWhenGrantedTargetIsMissing() {
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

        assertEquals("lobby", plugin.routeInitialConnection(
                event,
                AuthorizationMode.MONITOR,
                "lobby",
                decision));
        verify(event, never()).setResult(any());
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

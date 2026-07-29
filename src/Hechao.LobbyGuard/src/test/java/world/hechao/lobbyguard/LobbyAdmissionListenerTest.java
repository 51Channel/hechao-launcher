package world.hechao.lobbyguard;

import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;

import io.papermc.paper.event.connection.PlayerConnectionValidateLoginEvent;
import net.kyori.adventure.text.Component;
import org.bukkit.event.player.AsyncPlayerPreLoginEvent;
import org.junit.jupiter.api.Test;

final class LobbyAdmissionListenerTest {
    private final LobbyAdmissionListener listener =
            new LobbyAdmissionListener();

    @Test
    void rejectsAsyncPreLogin() {
        var event = mock(AsyncPlayerPreLoginEvent.class);

        listener.onAsyncPlayerPreLogin(event);

        verify(event).disallow(
                AsyncPlayerPreLoginEvent.Result.KICK_OTHER,
                listener.denialMessage());
    }

    @Test
    void rejectsSynchronousLoginAsASecondBoundary() {
        var event = mock(PlayerConnectionValidateLoginEvent.class);

        listener.onConnectionValidate(event);

        verify(event).kickMessage(listener.denialMessage());
    }

    @Test
    void denialMessageIsAlwaysPresent() {
        Component message = listener.denialMessage();

        org.junit.jupiter.api.Assertions.assertNotNull(message);
    }
}

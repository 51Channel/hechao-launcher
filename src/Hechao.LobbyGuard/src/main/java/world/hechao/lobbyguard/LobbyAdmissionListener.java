package world.hechao.lobbyguard;

import io.papermc.paper.event.connection.PlayerConnectionValidateLoginEvent;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.player.AsyncPlayerPreLoginEvent;

final class LobbyAdmissionListener implements Listener {
    private static final Component DENIAL_MESSAGE = Component.text(
            "大厅是内部基础设施节点，玩家不能进入。请从赫朝启动器选择服务器。",
            NamedTextColor.RED);

    @EventHandler(priority = EventPriority.MONITOR)
    public void onAsyncPlayerPreLogin(AsyncPlayerPreLoginEvent event) {
        event.disallow(
                AsyncPlayerPreLoginEvent.Result.KICK_OTHER,
                DENIAL_MESSAGE);
    }

    @EventHandler(priority = EventPriority.MONITOR)
    public void onConnectionValidate(
            PlayerConnectionValidateLoginEvent event) {
        event.kickMessage(DENIAL_MESSAGE);
    }

    Component denialMessage() {
        return DENIAL_MESSAGE;
    }
}

package world.hechao.lobbyguard;

import org.bukkit.plugin.java.JavaPlugin;

public final class HechaoLobbyGuardPlugin extends JavaPlugin {
    @Override
    public void onEnable() {
        var listener = new LobbyAdmissionListener();
        getServer().getPluginManager().registerEvents(listener, this);
        for (var player : getServer().getOnlinePlayers()) {
            player.kick(listener.denialMessage());
        }
        getLogger().info(
                "Player admission is disabled for this infrastructure Lobby.");
    }
}

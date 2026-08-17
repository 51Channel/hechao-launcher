package world.hechao.economy.placeholder;

import java.math.RoundingMode;
import me.clip.placeholderapi.expansion.PlaceholderExpansion;
import org.bukkit.OfflinePlayer;
import org.jetbrains.annotations.NotNull;
import org.jetbrains.annotations.Nullable;
import world.hechao.economy.HechaoEconomyPlugin;

public final class HechaoBalanceExpansion extends PlaceholderExpansion {
    private final HechaoEconomyPlugin plugin;

    public HechaoBalanceExpansion(HechaoEconomyPlugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public @NotNull String getIdentifier() {
        return "hechao";
    }

    @Override
    public @NotNull String getAuthor() {
        return "Hechao";
    }

    @Override
    public @NotNull String getVersion() {
        return plugin.getDescription().getVersion();
    }

    @Override
    public boolean persist() {
        return true;
    }

    @Override
    public @Nullable String onRequest(OfflinePlayer player, @NotNull String params) {
        if (!"balance".equalsIgnoreCase(params) || player == null) {
            return null;
        }
        return plugin.cachedBalance(player.getUniqueId())
                .map(value -> value.setScale(2, RoundingMode.HALF_UP).toPlainString())
                .orElse("--");
    }
}

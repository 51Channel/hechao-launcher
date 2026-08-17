package world.hechao.economy;

import java.util.List;
import java.util.function.Function;
import org.bukkit.command.PluginCommand;
import org.bukkit.plugin.Plugin;

final class CommandOwnershipVerifier {
    static final List<String> REQUIRED_COMMANDS = List.of(
            "money", "balance", "bal", "pay", "sell", "shop", "heco");

    private CommandOwnershipVerifier() {
    }

    static List<String> findConflicts(
            Plugin expectedOwner,
            Function<String, PluginCommand> commandResolver) {
        return REQUIRED_COMMANDS.stream()
                .filter(name -> {
                    var command = commandResolver.apply(name);
                    return command == null || command.getPlugin() != expectedOwner;
                })
                .toList();
    }
}

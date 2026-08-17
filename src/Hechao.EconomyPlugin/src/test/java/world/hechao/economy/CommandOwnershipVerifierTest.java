package world.hechao.economy;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import java.util.ArrayList;
import java.util.List;
import org.bukkit.command.PluginCommand;
import org.bukkit.plugin.Plugin;
import org.junit.jupiter.api.Test;

final class CommandOwnershipVerifierTest {
    @Test
    void acceptsEveryRootCommandAndAliasOwnedByThePlugin() {
        var owner = mock(Plugin.class);
        var command = commandOwnedBy(owner);
        var resolved = new ArrayList<String>();

        var conflicts = CommandOwnershipVerifier.findConflicts(owner, name -> {
            resolved.add(name);
            return command;
        });

        assertTrue(conflicts.isEmpty());
        assertEquals(CommandOwnershipVerifier.REQUIRED_COMMANDS, resolved);
    }

    @Test
    void rejectsMissingCommandsAndAliases() {
        var owner = mock(Plugin.class);
        var owned = commandOwnedBy(owner);

        var conflicts = CommandOwnershipVerifier.findConflicts(
                owner,
                name -> List.of("balance", "bal").contains(name) ? null : owned);

        assertEquals(List.of("balance", "bal"), conflicts);
    }

    @Test
    void rejectsCommandsOwnedByAnotherPlugin() {
        var owner = mock(Plugin.class);
        var otherOwner = mock(Plugin.class);
        var owned = commandOwnedBy(owner);
        var conflicting = commandOwnedBy(otherOwner);

        var conflicts = CommandOwnershipVerifier.findConflicts(
                owner,
                name -> "pay".equals(name) ? conflicting : owned);

        assertEquals(List.of("pay"), conflicts);
    }

    private static PluginCommand commandOwnedBy(Plugin owner) {
        var command = mock(PluginCommand.class);
        when(command.getPlugin()).thenReturn(owner);
        return command;
    }
}

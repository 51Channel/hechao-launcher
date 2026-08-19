package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class TeamCommandInputTest {
    @Test
    void acceptsOnlyMinecraftPlayerNames() {
        assertTrue(TeamCommandInput.acceptsPlayerName("Player_51"));
        assertTrue(TeamCommandInput.validPlayerName("Player_51"));
        assertFalse(TeamCommandInput.validPlayerName(""));
        assertFalse(TeamCommandInput.acceptsPlayerName("player name"));
        assertFalse(TeamCommandInput.acceptsPlayerName("abcdefghijklmnopq"));
    }

    @Test
    void trimsChatAndRejectsCommandBreakingNewlines() {
        assertEquals("准备出发", TeamCommandInput.normalizedChat("  准备出发  "));
        assertEquals("", TeamCommandInput.normalizedChat("第一行\n第二行"));
    }
}

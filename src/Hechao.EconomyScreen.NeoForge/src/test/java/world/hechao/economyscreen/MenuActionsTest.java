package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class MenuActionsTest {
    @Test
    void includesServerOwnerProductConfigurationAction() {
        var action = MenuActions.all().get("admin_product");

        assertEquals("服主回收设置", action.label());
        assertEquals("hechaoeconomy:heco product", action.command());
        assertEquals(true, action.administratorOnly());
    }

    @Test
    void openMenuPayloadCarriesOnlySessionAndActionIds() throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economyscreen",
                "network",
                "OpenMenuPayload.java"));

        assertTrue(source.contains("UUID sessionId"));
        assertTrue(source.contains("List<String> actionIds"));
        assertFalse(source.contains("String title"));
        assertFalse(source.contains("String subtitle"));
        assertFalse(source.contains("MenuButton"));
    }

    @Test
    void screenKeepsCompactScrollingLayoutForShortWindows() throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economyscreen",
                "client",
                "HechaoNavigationScreen.java"));

        assertTrue(source.contains("compact = height < 180"));
        assertTrue(source.contains("mouseScrolled"));
        assertTrue(source.contains("maximumScrollRow"));
        assertTrue(source.contains("plainSubstrByWidth"));
    }

    @Test
    void administratorActionsRelyOnBukkitPermissionInsteadOfMinecraftOpLevel()
            throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economyscreen",
                "HechaoEconomyScreenMod.java"));

        assertFalse(source.contains("player.hasPermissions(2)"));
        assertTrue(source.contains("Set.copyOf(actionIds)"));
        assertTrue(source.contains("performPrefixedCommand"));
    }
}

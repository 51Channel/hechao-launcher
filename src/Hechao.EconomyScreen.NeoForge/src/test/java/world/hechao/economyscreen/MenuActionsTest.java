package world.hechao.economyscreen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

final class MenuActionsTest {
    @Test
    void playerMenuDoesNotExposeServerOwnerConfiguration() {
        assertFalse(MenuActions.all().containsKey("admin_product"));
    }

    @Test
    void externalFeaturesUseTheirOwningPluginNamespace() {
        assertEquals(
                "skyrealmcore:settings",
                MenuActions.all().get("settings").command());
        assertEquals(
                "skyrealmcore:team list",
                MenuActions.all().get("team").command());
    }

    @Test
    void saleActionOpensPlayerMarketListingWorkflow() {
        var action = MenuActions.all().get("sell");

        assertEquals("上架物品", action.label());
        assertEquals("放入物品并设置玩家市场售价", action.description());
        assertEquals("hechaoeconomy:ah sell", action.command());
    }

    @Test
    void everyActionProvidesImmediateClientFeedback() {
        for (var action : MenuActions.all().entrySet()) {
            assertFalse(action.getValue().feedback().isBlank(), action.getKey());
            assertTrue(action.getValue().feedback().startsWith("正在"), action.getKey());
        }
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
    void screenUsesCompactShortcutGridWithoutDashboardChrome() throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economyscreen",
                "client",
                "HechaoNavigationScreen.java"));

        assertTrue(source.contains("NavigationLayout.calculate"));
        assertTrue(source.contains("NavigationLayout.BUTTON_HEIGHT"));
        assertTrue(source.contains("mouseScrolled"));
        assertTrue(source.contains("displayClientMessage"));
        assertTrue(source.contains("action.definition.feedback()"));
        assertTrue(source.contains("ClientEconomyUiBridge.openWaiting"));
        assertTrue(source.contains("ClientEconomyUiBridge.requestMarketListing()"));
        assertTrue(source.contains("actionIcon(action.actionId)"));
        assertFalse(source.contains("addServerAuthorizationIndicator"));
        assertFalse(source.contains("renderStatusLamp"));
        assertFalse(source.contains("graphics.fill("));
        assertFalse(source.contains("SUBTITLE"));
        assertFalse(source.contains("plainSubstrByWidth"));
    }

    @Test
    void externalActionsAreFilteredByTheirServerCommandRequirement()
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
        assertTrue(source.contains("node.canUse(player.createCommandSourceStack())"));
        assertTrue(source.contains("Set.copyOf(actionIds)"));
        assertTrue(source.contains("performPrefixedCommand"));
    }

    @Test
    void pauseMenuKeepsModsEntryAndRequestsServerAuthorizedMenu()
            throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economyscreen",
                "client",
                "ClientPauseMenuEntry.java"));

        assertTrue(source.contains("instanceof PauseScreen"));
        assertTrue(source.contains("pauseScreen.showsPauseMenu()"));
        assertTrue(source.contains("Component.translatable(\"fml.menu.mods\")"));
        assertTrue(source.contains("modsButton.setWidth(HALF_BUTTON_WIDTH)"));
        assertTrue(source.contains("event.addListener(hechaoButton)"));
        assertTrue(source.contains("sendCommand(\"hechaomenu economy\")"));
        assertFalse(source.contains("new HechaoNavigationScreen"));
    }
}

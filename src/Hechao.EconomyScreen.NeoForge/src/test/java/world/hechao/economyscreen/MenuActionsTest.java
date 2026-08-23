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
        assertEquals(
                "essentialsspawn:spawn",
                MenuActions.all().get("spawn").command());
        assertEquals(
                "hechaomenu rtp",
                MenuActions.all().get("rtp").command());
        assertEquals(
                "griefprevention:claimslist",
                MenuActions.all().get("claims").command());
    }

    @Test
    void saleActionOpensPlayerMarketListingWorkflow() {
        var action = MenuActions.all().get("sell");

        assertEquals("市场上架", action.label());
        assertEquals("玩家市场快捷入口：放入物品并设置售价", action.description());
        assertEquals("hechaoeconomy:ah sell", action.command());
    }

    @Test
    void commonThreeColumnLayoutGroupsPrimaryDestinationsBeforeShortcuts() {
        assertEquals(
                java.util.List.of(
                        "balance",
                        "shop",
                        "prices",
                        "market",
                        "sell",
                        "market_mine",
                        "market_claim",
                        "payment",
                        "team",
                        "teleport",
                        "home",
                        "spawn",
                        "rtp",
                        "back",
                        "claims",
                        "settings"),
                MenuActions.all().keySet().stream().toList());
        assertTrue(MenuActions.all().size() <= 16);
    }

    @Test
    void formActionsConsumeAuthorizationWithoutDirectServerExecution() {
        assertEquals(
                MenuActions.ExecutionMode.CLIENT_SCREEN,
                MenuActions.all().get("payment").executionMode());
        assertEquals(
                MenuActions.ExecutionMode.CLIENT_SCREEN,
                MenuActions.all().get("teleport").executionMode());
        assertEquals(
                MenuActions.ExecutionMode.SERVER,
                MenuActions.all().get("market").executionMode());
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
        assertTrue(source.contains("new MenuActionPayload(payload.sessionId(), action.actionId)"));
        assertFalse(source.contains("requestMarketListing()"));
        assertTrue(source.contains("if (actionSubmitted)"));
        assertTrue(source.contains("actionSubmitted = true"));
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
        assertTrue(source.contains("node.canUse(source)"));
        assertTrue(source.contains(".filter(entry -> isCommandUsable(player, entry.getValue().command()))"));
        assertFalse(source.contains("command().startsWith(\"hechaoeconomy:\")"));
        assertTrue(source.contains("Set.copyOf(actionIds)"));
        assertTrue(source.contains("event.registrar(\"3\")"));
        assertTrue(source.contains("Commands.literal(\"rtp\")"));
        assertTrue(source.contains("Commands.literal(\"setcity\")"));
        assertTrue(source.contains("source.hasPermission(2)"));
        assertTrue(source.contains("essentialsspawn:setspawn"));
        assertTrue(source.contains("RtpSafeLocationFinder.find("));
        assertTrue(source.contains("player.teleportTo(target.x(), target.y(), target.z())"));
        assertTrue(source.contains("player.setDeltaMovement(Vec3.ZERO)"));
        assertTrue(source.contains("player.resetFallDistance()"));
        assertTrue(source.contains("RTP_COOLDOWNS.release(player.getUUID())"));
        assertTrue(source.contains("action.executionMode() == MenuActions.ExecutionMode.SERVER"));
        assertTrue(source.contains("EconomyMessageProtocol.authorization("));
        assertTrue(source.contains("payload.sessionId()"));
        assertTrue(source.contains("case RATE_LIMITED"));
        assertTrue(source.contains("case ACTION_NOT_ALLOWED"));
        assertTrue(source.contains("[赫朝经济] 菜单已失效，请重新打开。"));
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
        assertTrue(source.contains("hasHechaoButton(event)"));
        assertTrue(source.contains("modsButton.getWidth() < HALF_BUTTON_WIDTH * 2 + BUTTON_GAP"));
        assertTrue(source.contains("int splitWidth = (modsButton.getWidth() - BUTTON_GAP) / 2"));
        assertTrue(source.contains("modsButton.setWidth(splitWidth)"));
        assertTrue(source.contains("event.addListener(hechaoButton)"));
        assertTrue(source.contains("sendCommand(\"hechaomenu economy\")"));
        assertFalse(source.contains("new HechaoNavigationScreen"));
    }
}

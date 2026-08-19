package world.hechao.economyscreen.client;

import java.util.Set;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.inventory.ContainerScreen;
import net.minecraft.network.chat.Component;
import net.neoforged.neoforge.client.event.ClientChatReceivedEvent;
import net.neoforged.neoforge.client.event.ScreenEvent;
import net.neoforged.neoforge.common.NeoForge;

public final class ClientEconomyUiBridge {
    static final String ECONOMY_PREFIX = "[赫朝经济]";
    static final String SETTINGS_TITLE = "天域设置";
    static final String CATALOG_TITLE = "赫朝回收目录";
    static final String SELL_TITLE = "赫朝物品回收";
    static final String MARKET_TITLE = "赫朝玩家市场";
    static final String MARKET_MINE_TITLE = "赫朝我的挂单";
    static final String MARKET_DELIVERY_TITLE = "赫朝待领取";
    static final String MARKET_LISTING_TITLE = "赫朝市场上架";
    static final String MARKET_PURCHASE_TITLE = "赫朝确认购买";
    static final String MARKET_CANCEL_TITLE = "赫朝确认下架";
    private static final Set<String> EMBEDDED_ACTIONS = Set.of(
            "balance",
            "shop",
            "sell",
            "market",
            "team");

    private ClientEconomyUiBridge() {
    }

    public static void register() {
        NeoForge.EVENT_BUS.addListener(ClientEconomyUiBridge::onSystemMessage);
        NeoForge.EVENT_BUS.addListener(ClientEconomyUiBridge::onScreenOpening);
    }

    static boolean opensEmbeddedScreen(String actionId) {
        return EMBEDDED_ACTIONS.contains(actionId);
    }

    static void openWaiting(
            String actionId,
            String title,
            String loadingMessage) {
        if ("team".equals(actionId)) {
            Minecraft.getInstance().setScreen(new TeamManagementScreen(
                    Component.literal(title)));
            return;
        }
        Minecraft.getInstance().setScreen(new EconomyResultScreen(
                actionId,
                Component.literal(title),
                loadingMessage));
    }

    static void requestHome() {
        var connection = Minecraft.getInstance().getConnection();
        if (connection != null) {
            connection.sendCommand("hechaomenu economy");
        }
    }

    private static void onSystemMessage(ClientChatReceivedEvent.System event) {
        var minecraft = Minecraft.getInstance();
        String message = event.getMessage().getString();
        if (!message.contains(ECONOMY_PREFIX)) {
            if (minecraft.screen instanceof TeamManagementScreen screen
                    && screen.acceptsSystemMessage(message)) {
                screen.acceptMessage(event.getMessage());
            } else if (minecraft.screen instanceof EconomyResultScreen screen
                    && screen.acceptsSystemMessage(message)) {
                screen.acceptMessage(event.getMessage());
            }
            return;
        }
        if (minecraft.screen instanceof EconomyResultScreen screen) {
            screen.acceptMessage(event.getMessage());
        } else if (minecraft.screen instanceof HechaoNavigationScreen screen) {
            screen.acceptEconomyMessage(message);
        }
    }

    private static void onScreenOpening(ScreenEvent.Opening event) {
        var next = event.getNewScreen();
        if (next instanceof ContainerScreen container
                && SETTINGS_TITLE.equals(next.getTitle().getString())) {
            event.setNewScreen(new SkyrealmSettingsScreen(container.getMenu()));
        } else if (next instanceof ContainerScreen container
                && CATALOG_TITLE.equals(next.getTitle().getString())) {
            event.setNewScreen(new EconomyCatalogScreen(container.getMenu()));
        } else if (next instanceof ContainerScreen container
                && SELL_TITLE.equals(next.getTitle().getString())) {
            var player = Minecraft.getInstance().player;
            if (player != null) {
                event.setNewScreen(new EconomySellScreen(
                        container.getMenu(),
                        player.getInventory()));
            }
        } else if (next instanceof ContainerScreen container
                && isMarketListTitle(next.getTitle().getString())) {
            event.setNewScreen(new EconomyMarketScreen(
                    container.getMenu(),
                    next.getTitle().getString()));
        } else if (next instanceof ContainerScreen container
                && MARKET_LISTING_TITLE.equals(next.getTitle().getString())) {
            var player = Minecraft.getInstance().player;
            if (player != null) {
                event.setNewScreen(new EconomyMarketListingScreen(
                        container.getMenu(),
                        player.getInventory()));
            }
        } else if (next instanceof ContainerScreen container
                && isMarketDecisionTitle(next.getTitle().getString())) {
            event.setNewScreen(new EconomyMarketDecisionScreen(
                    container.getMenu(),
                    next.getTitle().getString()));
        }
    }

    private static boolean isMarketListTitle(String title) {
        return MARKET_TITLE.equals(title)
                || MARKET_MINE_TITLE.equals(title)
                || MARKET_DELIVERY_TITLE.equals(title);
    }

    private static boolean isMarketDecisionTitle(String title) {
        return MARKET_PURCHASE_TITLE.equals(title)
                || MARKET_CANCEL_TITLE.equals(title);
    }
}

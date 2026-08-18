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
    static final String CATALOG_TITLE = "赫朝回收目录";
    private static final Set<String> EMBEDDED_ACTIONS = Set.of(
            "balance",
            "shop",
            "sell");

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
                && CATALOG_TITLE.equals(next.getTitle().getString())) {
            event.setNewScreen(new EconomyCatalogScreen(container.getMenu()));
        }
    }
}

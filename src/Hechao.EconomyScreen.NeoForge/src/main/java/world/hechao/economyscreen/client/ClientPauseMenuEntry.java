package world.hechao.economyscreen.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.network.chat.Component;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.api.distmarker.OnlyIn;
import net.neoforged.neoforge.client.event.ScreenEvent;
import net.neoforged.neoforge.common.NeoForge;

@OnlyIn(Dist.CLIENT)
public final class ClientPauseMenuEntry {
    private static final Component MODS_LABEL = Component.translatable("fml.menu.mods");
    private static final Component HECHAO_LABEL = Component.translatable(
            "hechao_economy_screen.pause_menu");
    private static final int HALF_BUTTON_WIDTH = 98;
    private static final int BUTTON_GAP = 8;

    private ClientPauseMenuEntry() {
    }

    public static void register() {
        NeoForge.EVENT_BUS.addListener(ClientPauseMenuEntry::onScreenInitialized);
    }

    private static void onScreenInitialized(ScreenEvent.Init.Post event) {
        if (!(event.getScreen() instanceof PauseScreen pauseScreen)
                || !pauseScreen.showsPauseMenu()) {
            return;
        }
        if (hasHechaoButton(event)) {
            return;
        }

        var modsButton = event.getListenersList().stream()
                .filter(Button.class::isInstance)
                .map(Button.class::cast)
                .filter(button -> button.getMessage().equals(MODS_LABEL))
                .findFirst()
                .orElse(null);
        if (modsButton == null
                || modsButton.getWidth() < HALF_BUTTON_WIDTH * 2 + BUTTON_GAP) {
            return;
        }

        int splitWidth = (modsButton.getWidth() - BUTTON_GAP) / 2;
        modsButton.setWidth(splitWidth);
        var hechaoButton = Button.builder(
                        HECHAO_LABEL,
                        ignored -> requestServerMenu())
                .bounds(
                        modsButton.getX() + splitWidth + BUTTON_GAP,
                        modsButton.getY(),
                        splitWidth,
                        modsButton.getHeight())
                .build();
        hechaoButton.active = Minecraft.getInstance().getConnection() != null;
        event.addListener(hechaoButton);
    }

    private static boolean hasHechaoButton(ScreenEvent.Init.Post event) {
        return event.getListenersList().stream()
                .filter(Button.class::isInstance)
                .map(Button.class::cast)
                .anyMatch(button -> button.getMessage().equals(HECHAO_LABEL));
    }

    private static void requestServerMenu() {
        var connection = Minecraft.getInstance().getConnection();
        if (connection != null) {
            connection.sendCommand("hechaomenu economy");
        }
    }
}

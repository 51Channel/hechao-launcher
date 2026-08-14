package world.hechao.economyscreen.client;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.neoforged.neoforge.network.PacketDistributor;
import world.hechao.economyscreen.network.MenuActionPayload;
import world.hechao.economyscreen.network.OpenMenuPayload;

public final class HechaoNavigationScreen extends Screen {
    private static final int PANEL_WIDTH = 380;
    private static final int BUTTON_WIDTH = 168;
    private static final int BUTTON_HEIGHT = 34;

    private final OpenMenuPayload payload;
    private final List<ButtonRow> rows = new ArrayList<>();

    public HechaoNavigationScreen(OpenMenuPayload payload) {
        super(Component.literal(payload.title()));
        this.payload = payload;
    }

    @Override
    protected void init() {
        rows.clear();
        int left = (width - PANEL_WIDTH) / 2;
        int top = Math.max(42, (height - 244) / 2);
        for (int index = 0; index < payload.buttons().size(); index++) {
            var definition = payload.buttons().get(index);
            int column = index % 2;
            int row = index / 2;
            int x = left + 16 + column * (BUTTON_WIDTH + 12);
            int y = top + 76 + row * 52;
            var button = Button.builder(
                            Component.literal(definition.label()),
                            ignored -> sendAction(definition.actionId()))
                    .bounds(x, y, BUTTON_WIDTH, BUTTON_HEIGHT)
                    .build();
            addRenderableWidget(button);
            rows.add(new ButtonRow(
                    definition.description(),
                    x,
                    y + BUTTON_HEIGHT + 2));
        }
    }

    @Override
    public void render(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        renderBackground(graphics, mouseX, mouseY, partialTick);
        int left = (width - PANEL_WIDTH) / 2;
        int top = Math.max(42, (height - 244) / 2);
        int panelHeight = Math.max(
                206,
                98 + ((payload.buttons().size() + 1) / 2) * 52);
        graphics.fill(left, top, left + PANEL_WIDTH, top + panelHeight, 0xED111417);
        graphics.fill(left, top, left + 4, top + panelHeight, 0xFFE5A93D);
        graphics.drawString(font, title, left + 18, top + 18, 0xFFF5F2EB, false);
        graphics.drawString(
                font,
                Component.literal(payload.subtitle()),
                left + 18,
                top + 40,
                0xFFAAB0B7,
                false);
        for (var row : rows) {
            graphics.drawString(
                    font,
                    Component.literal(row.description),
                    row.x,
                    row.y,
                    0xFF8F969E,
                    false);
        }
        super.render(graphics, mouseX, mouseY, partialTick);
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private void sendAction(String actionId) {
        PacketDistributor.sendToServer(new MenuActionPayload(payload.sessionId(), actionId));
        onClose();
    }

    private record ButtonRow(String description, int x, int y) {
    }
}

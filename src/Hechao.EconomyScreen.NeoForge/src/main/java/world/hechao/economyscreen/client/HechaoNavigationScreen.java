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
    private static final int MAX_PANEL_WIDTH = 380;
    private static final int BUTTON_HEIGHT = 30;
    private static final int ROW_HEIGHT = 46;

    private final OpenMenuPayload payload;
    private final List<ButtonRow> rows = new ArrayList<>();

    public HechaoNavigationScreen(OpenMenuPayload payload) {
        super(Component.literal(payload.title()));
        this.payload = payload;
    }

    @Override
    protected void init() {
        rows.clear();
        int panelWidth = panelWidth();
        int panelHeight = panelHeight();
        int buttonWidth = (panelWidth - 44) / 2;
        int left = (width - panelWidth) / 2;
        int top = Math.max(10, (height - panelHeight) / 2);
        for (int index = 0; index < payload.buttons().size(); index++) {
            var definition = payload.buttons().get(index);
            int column = index % 2;
            int row = index / 2;
            int x = left + 16 + column * (buttonWidth + 12);
            int y = top + 62 + row * ROW_HEIGHT;
            var button = Button.builder(
                            Component.literal(definition.label()),
                            ignored -> sendAction(definition.actionId()))
                    .bounds(x, y, buttonWidth, BUTTON_HEIGHT)
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
        int panelWidth = panelWidth();
        int panelHeight = panelHeight();
        int left = (width - panelWidth) / 2;
        int top = Math.max(10, (height - panelHeight) / 2);
        graphics.fill(left, top, left + panelWidth, top + panelHeight, 0xED111417);
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

    private int panelWidth() {
        return Math.max(200, Math.min(MAX_PANEL_WIDTH, width - 24));
    }

    private int panelHeight() {
        int rowCount = (payload.buttons().size() + 1) / 2;
        return 76 + rowCount * ROW_HEIGHT;
    }

    private record ButtonRow(String description, int x, int y) {
    }
}

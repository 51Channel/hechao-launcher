package world.hechao.economyscreen.client;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.network.chat.Component;

final class EconomyResultScreen extends SinglePassBackgroundScreen {
    private static final int PANEL_MAX_WIDTH = 380;
    private static final int PANEL_HEIGHT = 164;
    private static final int BUTTON_MAX_WIDTH = 110;
    private static final int BUTTON_HEIGHT = 20;
    private static final int GAP = 8;

    private final EconomyResultState state;
    private Button confirmButton;
    private Button closeButton;
    private int panelLeft;
    private int panelTop;
    private int panelWidth;
    private int panelHeight;
    private int buttonWidth;

    EconomyResultScreen(
            String actionId,
            Component title,
            String loadingMessage) {
        super(title);
        state = new EconomyResultState(actionId, loadingMessage);
    }

    @Override
    protected void init() {
        panelWidth = Math.min(PANEL_MAX_WIDTH, width - 24);
        panelHeight = Math.min(PANEL_HEIGHT, height - 20);
        panelLeft = (width - panelWidth) / 2;
        panelTop = (height - panelHeight) / 2;
        buttonWidth = Math.min(
                BUTTON_MAX_WIDTH,
                (panelWidth - 24 - GAP) / 2);
        int buttonY = panelTop + panelHeight - BUTTON_HEIGHT - 12;

        confirmButton = new IndustrialButton(
                panelLeft + 12,
                buttonY,
                buttonWidth,
                BUTTON_HEIGHT,
                Component.literal("确认出售"),
                ignored -> confirmSale());
        addRenderableWidget(confirmButton);

        closeButton = new IndustrialButton(
                width / 2 - buttonWidth / 2,
                buttonY,
                buttonWidth,
                BUTTON_HEIGHT,
                Component.literal("完成"),
                ignored -> onClose());
        addRenderableWidget(closeButton);
        syncButtons();
    }

    void acceptMessage(Component message) {
        state.accept(message.getString());
        syncButtons();
    }

    @Override
    public void tick() {
        state.tick();
        syncButtons();
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        IndustrialUiTheme.renderPanel(
                graphics,
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight);
        IndustrialUiTheme.renderEmblem(
                graphics,
                panelLeft + 10,
                panelTop + 6,
                22);
        graphics.drawString(
                font,
                title,
                panelLeft + 39,
                panelTop + 12,
                0xFFFFFFFF,
                true);

        int color = switch (state.tone()) {
            case LOADING -> 0xFFFFD75A;
            case SUCCESS -> 0xFF8CD99B;
            case ERROR -> 0xFFFF8A80;
        };
        IndustrialUiTheme.renderStatusRail(
                graphics,
                panelLeft + 13,
                panelTop + 47,
                panelTop + panelHeight - 43,
                color);
        int textY = panelTop + 49;
        if (state.messages().isEmpty()) {
            graphics.drawCenteredString(
                    font,
                    state.loadingMessage(),
                    width / 2,
                    textY + 18,
                    color);
        } else {
            for (var message : state.messages()) {
                for (var line : font.split(
                        Component.literal(message),
                        panelWidth - 28)) {
                    graphics.drawString(
                            font,
                            line,
                            panelLeft + 22,
                            textY,
                            color,
                            false);
                    textY += 12;
                    if (textY >= panelTop + panelHeight - 42) {
                        break;
                    }
                }
                if (textY >= panelTop + panelHeight - 42) {
                    break;
                }
            }
        }
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private void confirmSale() {
        var connection = minecraft == null ? null : minecraft.getConnection();
        if (connection == null) {
            state.accept("经济连接已经断开。");
            return;
        }
        state.begin("正在确认出售...");
        connection.sendCommand("hechaoeconomy:sell confirm");
        syncButtons();
    }

    private void syncButtons() {
        if (confirmButton == null || closeButton == null) {
            return;
        }
        confirmButton.visible = state.canConfirmSale();
        closeButton.setX(confirmButton.visible
                ? panelLeft + panelWidth - 12 - buttonWidth
                : width / 2 - buttonWidth / 2);
    }
}

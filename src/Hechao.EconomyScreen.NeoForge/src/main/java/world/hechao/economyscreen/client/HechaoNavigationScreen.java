package world.hechao.economyscreen.client;

import java.util.List;
import net.minecraft.ChatFormatting;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import net.neoforged.neoforge.network.PacketDistributor;
import world.hechao.economyscreen.MenuActions;
import world.hechao.economyscreen.network.MenuActionPayload;
import world.hechao.economyscreen.network.OpenMenuPayload;

public final class HechaoNavigationScreen extends SinglePassBackgroundScreen {
    private static final Component TITLE = Component.literal("天域远征");
    private static final Component SERVER_AUTHORIZED_TOOLTIP = Component.literal(
            "菜单内容和权限由服务器决定");
    private static final int TITLE_EMBLEM_SIZE = 22;
    private static final int TITLE_EMBLEM_GAP = 7;
    private static final int TITLE_INDICATOR_GAP = 8;
    private static final int TITLE_INDICATOR_SIZE = 20;
    private static final int NAVIGATION_BUTTON_WIDTH = 98;

    private final OpenMenuPayload payload;
    private final List<ActionView> actions;
    private int scrollRow;
    private NavigationLayout.Layout layout;

    public HechaoNavigationScreen(OpenMenuPayload payload) {
        super(TITLE);
        this.payload = payload;
        this.actions = payload.actionIds().stream()
                .distinct()
                .map(actionId -> MenuActions.find(actionId)
                        .map(definition -> new ActionView(actionId, definition))
                        .orElse(null))
                .filter(java.util.Objects::nonNull)
                .toList();
    }

    @Override
    protected void init() {
        layout = NavigationLayout.calculate(width, height, actions.size(), scrollRow);
        scrollRow = layout.scrollRow();
        addServerAuthorizationIndicator();

        int firstIndex = scrollRow * layout.columns();
        int lastIndex = Math.min(
                actions.size(),
                (scrollRow + layout.visibleRows()) * layout.columns());
        for (int index = firstIndex; index < lastIndex; index++) {
            var action = actions.get(index);
            int relativeIndex = index - firstIndex;
            int column = relativeIndex % layout.columns();
            int row = relativeIndex / layout.columns();
            int x = layout.gridLeft()
                    + column * (layout.buttonWidth() + NavigationLayout.COLUMN_GAP);
            int y = layout.gridTop() + row * NavigationLayout.ROW_STRIDE;
            var button = new IndustrialButton(
                    x,
                    y,
                    layout.buttonWidth(),
                    NavigationLayout.BUTTON_HEIGHT,
                    Component.literal(action.definition.label()),
                    ignored -> sendAction(action));
            addRenderableWidget(button);
        }
        if (layout.needsNavigation()) {
            addNavigationButtons();
        }
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        int contentBottom = layout.needsNavigation()
                ? layout.navigationTop() + NavigationLayout.NAVIGATION_HEIGHT
                : layout.gridTop() + layout.gridHeight();
        int panelLeft = Math.max(6, layout.gridLeft() - 14);
        int panelTop = Math.max(4, layout.titleTop() - 10);
        int panelRight = Math.min(width - 6,
                layout.gridLeft() + layout.gridWidth() + 14);
        int panelBottom = Math.min(height - 4, contentBottom + 14);
        IndustrialUiTheme.renderPanel(
                graphics,
                panelLeft,
                panelTop,
                panelRight - panelLeft,
                panelBottom - panelTop);

        int titleWidth = font.width(TITLE);
        int groupWidth = TITLE_EMBLEM_SIZE
                + TITLE_EMBLEM_GAP
                + titleWidth
                + TITLE_INDICATOR_GAP
                + TITLE_INDICATOR_SIZE;
        int groupLeft = (width - groupWidth) / 2;
        IndustrialUiTheme.renderEmblem(
                graphics,
                groupLeft,
                layout.titleTop() - 1,
                TITLE_EMBLEM_SIZE);
        graphics.drawString(
                font,
                TITLE,
                groupLeft + TITLE_EMBLEM_SIZE + TITLE_EMBLEM_GAP,
                layout.titleTop() + 6,
                0xFFFFFFFF,
                true);
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    @Override
    public boolean mouseScrolled(
            double mouseX,
            double mouseY,
            double scrollX,
            double scrollY) {
        if (scrollY == 0 || layout.maximumScrollRow() == 0) {
            return super.mouseScrolled(mouseX, mouseY, scrollX, scrollY);
        }
        changePage(scrollY > 0 ? -1 : 1);
        return true;
    }

    private void sendAction(ActionView action) {
        PacketDistributor.sendToServer(
                new MenuActionPayload(payload.sessionId(), action.actionId));
        if (ClientEconomyUiBridge.opensEmbeddedScreen(action.actionId)) {
            ClientEconomyUiBridge.openWaiting(
                    action.actionId,
                    action.definition.label(),
                    action.definition.feedback());
            return;
        }
        var player = Minecraft.getInstance().player;
        if (player != null) {
            player.displayClientMessage(
                    Component.literal(action.definition.feedback())
                            .withStyle(ChatFormatting.YELLOW),
                    true);
        }
        onClose();
    }

    private void addServerAuthorizationIndicator() {
        int titleWidth = font.width(TITLE);
        int groupWidth = TITLE_EMBLEM_SIZE
                + TITLE_EMBLEM_GAP
                + titleWidth
                + TITLE_INDICATOR_GAP
                + TITLE_INDICATOR_SIZE;
        int indicatorLeft = (width - groupWidth) / 2
                + TITLE_EMBLEM_SIZE
                + TITLE_EMBLEM_GAP
                + titleWidth
                + TITLE_INDICATOR_GAP;
        var indicator = new IndustrialButton(
                indicatorLeft,
                layout.titleTop(),
                TITLE_INDICATOR_SIZE,
                TITLE_INDICATOR_SIZE,
                Component.literal("!").withStyle(ChatFormatting.YELLOW),
                ignored -> {
                });
        indicator.setTooltip(Tooltip.create(SERVER_AUTHORIZED_TOOLTIP));
        addRenderableWidget(indicator);
    }

    private void addNavigationButtons() {
        int navigationWidth = NAVIGATION_BUTTON_WIDTH * 2 + NavigationLayout.COLUMN_GAP;
        int navigationLeft = (width - navigationWidth) / 2;
        var previousButton = new IndustrialButton(
                navigationLeft,
                layout.navigationTop(),
                NAVIGATION_BUTTON_WIDTH,
                NavigationLayout.NAVIGATION_HEIGHT,
                Component.literal("上一页"),
                ignored -> changePage(-1));
        previousButton.active = scrollRow > 0;
        addRenderableWidget(previousButton);

        var nextButton = new IndustrialButton(
                navigationLeft + NAVIGATION_BUTTON_WIDTH + NavigationLayout.COLUMN_GAP,
                layout.navigationTop(),
                NAVIGATION_BUTTON_WIDTH,
                NavigationLayout.NAVIGATION_HEIGHT,
                Component.literal("下一页"),
                ignored -> changePage(1));
        nextButton.active = scrollRow < layout.maximumScrollRow();
        addRenderableWidget(nextButton);
    }

    private void changePage(int direction) {
        int next = Math.max(
                0,
                Math.min(
                        layout.maximumScrollRow(),
                        scrollRow + direction * layout.visibleRows()));
        if (next == scrollRow) {
            return;
        }
        scrollRow = next;
        clearWidgets();
        init();
    }

    private record ActionView(
            String actionId,
            MenuActions.Definition definition) {
    }

}

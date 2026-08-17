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
import world.hechao.economyscreen.MenuActions;

public final class HechaoNavigationScreen extends Screen {
    private static final Component TITLE = Component.literal("天域远征");
    private static final Component SUBTITLE = Component.literal("经济与生存功能");
    private static final int MAX_PANEL_WIDTH = 420;
    private static final int NORMAL_BUTTON_HEIGHT = 30;
    private static final int NORMAL_ROW_HEIGHT = 46;
    private static final int NORMAL_HEADER_HEIGHT = 62;
    private static final int NORMAL_NAVIGATION_HEIGHT = 28;
    private static final int COMPACT_BUTTON_HEIGHT = 24;
    private static final int COMPACT_ROW_HEIGHT = 34;
    private static final int COMPACT_HEADER_HEIGHT = 40;
    private static final int COMPACT_NAVIGATION_HEIGHT = 22;

    private final OpenMenuPayload payload;
    private final List<ActionView> actions;
    private final List<ButtonRow> rows = new ArrayList<>();
    private int scrollRow;
    private int columns;
    private int visibleRows;
    private int totalRows;
    private int panelWidth;
    private int panelHeight;
    private int panelLeft;
    private int panelTop;
    private int buttonHeight;
    private int rowHeight;
    private int headerHeight;
    private int navigationHeight;
    private boolean compact;

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
        rows.clear();
        panelWidth = panelWidth();
        compact = height < 180;
        buttonHeight = compact ? COMPACT_BUTTON_HEIGHT : NORMAL_BUTTON_HEIGHT;
        rowHeight = compact ? COMPACT_ROW_HEIGHT : NORMAL_ROW_HEIGHT;
        headerHeight = compact ? COMPACT_HEADER_HEIGHT : NORMAL_HEADER_HEIGHT;
        navigationHeight = compact
                ? COMPACT_NAVIGATION_HEIGHT
                : NORMAL_NAVIGATION_HEIGHT;
        columns = panelWidth >= 300 ? 2 : 1;
        totalRows = Math.max(1, (actions.size() + columns - 1) / columns);
        int preferredHeight = headerHeight + totalRows * rowHeight + 14;
        panelHeight = Math.min(preferredHeight, Math.max(100, height - 20));
        boolean needsNavigation = preferredHeight > panelHeight;
        int contentHeight = panelHeight - headerHeight
                - (needsNavigation ? navigationHeight : 10);
        visibleRows = Math.max(1, contentHeight / rowHeight);
        scrollRow = Math.max(0, Math.min(scrollRow, maximumScrollRow()));
        panelLeft = (width - panelWidth) / 2;
        panelTop = Math.max(10, (height - panelHeight) / 2);
        int horizontalGap = columns == 2 ? 12 : 0;
        int buttonWidth = (panelWidth - 32 - horizontalGap) / columns;
        int firstIndex = scrollRow * columns;
        int lastIndex = Math.min(actions.size(), (scrollRow + visibleRows) * columns);
        for (int index = firstIndex; index < lastIndex; index++) {
            var action = actions.get(index);
            int relativeIndex = index - firstIndex;
            int column = relativeIndex % columns;
            int row = relativeIndex / columns;
            int x = panelLeft + 16 + column * (buttonWidth + horizontalGap);
            int y = panelTop + headerHeight + row * rowHeight;
            var button = Button.builder(
                            Component.literal(action.definition.label()),
                            ignored -> sendAction(action.actionId))
                    .bounds(x, y, buttonWidth, buttonHeight)
                    .build();
            addRenderableWidget(button);
            if (!compact) {
                rows.add(new ButtonRow(
                        action.definition.description(),
                        x,
                        y + buttonHeight + 2,
                        buttonWidth));
            }
        }
        if (needsNavigation) {
            int navigationButtonHeight = compact ? 16 : 20;
            int y = panelTop + panelHeight - navigationButtonHeight - 4;
            addRenderableWidget(Button.builder(
                            Component.literal("↑"),
                            ignored -> changePage(-1))
                    .bounds(
                            panelLeft + panelWidth - 68,
                            y,
                            24,
                            navigationButtonHeight)
                    .build());
            addRenderableWidget(Button.builder(
                            Component.literal("↓"),
                            ignored -> changePage(1))
                    .bounds(
                            panelLeft + panelWidth - 40,
                            y,
                            24,
                            navigationButtonHeight)
                    .build());
        }
    }

    @Override
    public void render(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        renderBackground(graphics, mouseX, mouseY, partialTick);
        graphics.fill(
                panelLeft,
                panelTop,
                panelLeft + panelWidth,
                panelTop + panelHeight,
                0xED111417);
        graphics.fill(
                panelLeft,
                panelTop,
                panelLeft + 4,
                panelTop + panelHeight,
                0xFFE5A93D);
        graphics.drawString(
                font,
                TITLE,
                panelLeft + 18,
                panelTop + 18,
                0xFFF5F2EB,
                false);
        if (!compact) {
            graphics.drawString(
                    font,
                    SUBTITLE,
                    panelLeft + 18,
                    panelTop + 40,
                    0xFFAAB0B7,
                    false);
        }
        for (var row : rows) {
            var text = font.plainSubstrByWidth(row.description, row.maximumWidth);
            graphics.drawString(
                    font,
                    Component.literal(text),
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

    @Override
    public boolean mouseScrolled(
            double mouseX,
            double mouseY,
            double scrollX,
            double scrollY) {
        if (scrollY == 0 || maximumScrollRow() == 0) {
            return super.mouseScrolled(mouseX, mouseY, scrollX, scrollY);
        }
        changePage(scrollY > 0 ? -1 : 1);
        return true;
    }

    private void sendAction(String actionId) {
        PacketDistributor.sendToServer(new MenuActionPayload(payload.sessionId(), actionId));
        onClose();
    }

    private int panelWidth() {
        return Math.max(200, Math.min(MAX_PANEL_WIDTH, width - 24));
    }

    private int maximumScrollRow() {
        return Math.max(0, totalRows - visibleRows);
    }

    private void changePage(int direction) {
        int next = Math.max(0, Math.min(maximumScrollRow(), scrollRow + direction));
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

    private record ButtonRow(
            String description,
            int x,
            int y,
            int maximumWidth) {
    }
}

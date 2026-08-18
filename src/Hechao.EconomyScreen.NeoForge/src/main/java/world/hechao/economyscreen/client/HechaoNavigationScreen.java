package world.hechao.economyscreen.client;

import java.util.List;
import net.minecraft.ChatFormatting;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.neoforged.neoforge.network.PacketDistributor;
import world.hechao.economyscreen.MenuActions;
import world.hechao.economyscreen.network.MenuActionPayload;
import world.hechao.economyscreen.network.OpenMenuPayload;

public final class HechaoNavigationScreen extends Screen {
    private static final Component TITLE = Component.literal("天域远征");
    private static final Component SERVER_AUTHORIZED_TOOLTIP = Component.literal(
            "菜单内容和权限由服务器决定");
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
            var button = Button.builder(
                            Component.literal(action.definition.label()),
                            ignored -> sendAction(action.actionId))
                    .bounds(
                            x,
                            y,
                            layout.buttonWidth(),
                            NavigationLayout.BUTTON_HEIGHT)
                    .build();
            addRenderableWidget(button);
        }
        if (layout.needsNavigation()) {
            addNavigationButtons();
        }
    }

    @Override
    public void render(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        renderBackground(graphics, mouseX, mouseY, partialTick);
        int titleWidth = font.width(TITLE);
        int groupWidth = titleWidth + TITLE_INDICATOR_GAP + TITLE_INDICATOR_SIZE;
        int groupLeft = (width - groupWidth) / 2;
        graphics.drawString(
                font,
                TITLE,
                groupLeft,
                layout.titleTop() + 6,
                0xFFFFFFFF,
                true);
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
        if (scrollY == 0 || layout.maximumScrollRow() == 0) {
            return super.mouseScrolled(mouseX, mouseY, scrollX, scrollY);
        }
        changePage(scrollY > 0 ? -1 : 1);
        return true;
    }

    private void sendAction(String actionId) {
        PacketDistributor.sendToServer(new MenuActionPayload(payload.sessionId(), actionId));
        onClose();
    }

    private void addServerAuthorizationIndicator() {
        int titleWidth = font.width(TITLE);
        int groupWidth = titleWidth + TITLE_INDICATOR_GAP + TITLE_INDICATOR_SIZE;
        int indicatorLeft = (width - groupWidth) / 2
                + titleWidth
                + TITLE_INDICATOR_GAP;
        addRenderableWidget(Button.builder(
                        Component.literal("!").withStyle(ChatFormatting.YELLOW),
                        ignored -> {
                        })
                .bounds(
                        indicatorLeft,
                        layout.titleTop(),
                        TITLE_INDICATOR_SIZE,
                        TITLE_INDICATOR_SIZE)
                .tooltip(Tooltip.create(SERVER_AUTHORIZED_TOOLTIP))
                .build());
    }

    private void addNavigationButtons() {
        int navigationWidth = NAVIGATION_BUTTON_WIDTH * 2 + NavigationLayout.COLUMN_GAP;
        int navigationLeft = (width - navigationWidth) / 2;
        var previousButton = Button.builder(
                        Component.literal("上一页"),
                        ignored -> changePage(-1))
                .bounds(
                        navigationLeft,
                        layout.navigationTop(),
                        NAVIGATION_BUTTON_WIDTH,
                        NavigationLayout.NAVIGATION_HEIGHT)
                .build();
        previousButton.active = scrollRow > 0;
        addRenderableWidget(previousButton);

        var nextButton = Button.builder(
                        Component.literal("下一页"),
                        ignored -> changePage(1))
                .bounds(
                        navigationLeft + NAVIGATION_BUTTON_WIDTH + NavigationLayout.COLUMN_GAP,
                        layout.navigationTop(),
                        NAVIGATION_BUTTON_WIDTH,
                        NavigationLayout.NAVIGATION_HEIGHT)
                .build();
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

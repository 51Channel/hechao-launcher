package world.hechao.economyscreen.client;

import java.util.List;
import net.minecraft.ChatFormatting;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.neoforged.neoforge.network.PacketDistributor;
import world.hechao.economyscreen.MenuActions;
import world.hechao.economyscreen.network.MenuActionPayload;
import world.hechao.economyscreen.network.OpenMenuPayload;

public final class HechaoNavigationScreen extends SinglePassBackgroundScreen {
    private static final Component TITLE = Component.literal("天域远征");
    private static final int TITLE_EMBLEM_SIZE = 18;
    private static final int TITLE_EMBLEM_GAP = 5;
    private static final int NAVIGATION_BUTTON_WIDTH = 98;
    private static final int RETURN_BUTTON_WIDTH = 88;

    private final OpenMenuPayload payload;
    private final List<ActionView> actions;
    private int scrollRow;
    private NavigationLayout.Layout layout;
    private EconomyResultPresentation.Balance balance;
    private boolean balanceRequested;

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
        requestBalance();

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
                    actionIcon(action.actionId),
                    ignored -> sendAction(action));
            button.setTooltip(Tooltip.create(Component.literal(
                    action.definition.description())));
            addRenderableWidget(button);
        }
        if (layout.needsNavigation()) {
            addNavigationButtons();
        }
        int returnWidth = layout.sharedFooter() ? 68 : RETURN_BUTTON_WIDTH;
        addRenderableWidget(new IndustrialButton(
                width / 2 - returnWidth / 2,
                layout.returnTop(),
                returnWidth,
                NavigationLayout.RETURN_HEIGHT,
                Component.literal("返回游戏"),
                ignored -> onClose()));
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        var panel = panelBounds();
        IndustrialUiTheme.renderPanel(
                graphics,
                panel.left(),
                panel.top(),
                panel.width(),
                panel.height(),
                28);

        int groupLeft = panel.left() + 10;
        IndustrialUiTheme.renderEmblem(
                graphics,
                groupLeft,
                layout.titleTop() + 1,
                TITLE_EMBLEM_SIZE);
        graphics.drawString(
                font,
                TITLE,
                groupLeft + TITLE_EMBLEM_SIZE + TITLE_EMBLEM_GAP,
                layout.titleTop() + 6,
                0xFFFFFFFF,
                true);
        renderBalance(graphics, panel);
    }

    void acceptEconomyMessage(String message) {
        EconomyResultPresentation.balance(message).ifPresent(value -> balance = value);
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
        if ("sell".equals(action.actionId)) {
            if (!ClientEconomyUiBridge.requestMarketListing()) {
                return;
            }
        } else {
            PacketDistributor.sendToServer(
                    new MenuActionPayload(payload.sessionId(), action.actionId));
        }
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

    private void requestBalance() {
        if (balanceRequested) {
            return;
        }
        var connection = Minecraft.getInstance().getConnection();
        if (connection == null) {
            return;
        }
        balanceRequested = true;
        connection.sendCommand("hechaoeconomy:money");
    }

    private void renderBalance(GuiGraphics graphics, PanelBounds panel) {
        String text = balance == null
                ? "余额同步中"
                : balance.amount() + " 金币";
        int textWidth = font.width(text);
        int textX = panel.left() + panel.width() - 12 - textWidth;
        int titleRight = panel.left() + 10
                + TITLE_EMBLEM_SIZE
                + TITLE_EMBLEM_GAP
                + font.width(TITLE);
        if (textX <= titleRight + 6) {
            return;
        }
        graphics.drawString(
                font,
                text,
                textX,
                layout.titleTop() + 6,
                balance == null ? 0xFFC6B46F : 0xFFFFD66B,
                false);
    }

    private void addNavigationButtons() {
        if (layout.sharedFooter()) {
            var panel = panelBounds();
            var previousButton = new IndustrialButton(
                    panel.left() + 8,
                    layout.navigationTop(),
                    30,
                    NavigationLayout.NAVIGATION_HEIGHT,
                    Component.literal("<"),
                    ignored -> changePage(-1));
            previousButton.active = scrollRow > 0;
            previousButton.setTooltip(Tooltip.create(Component.literal("上一页")));
            addRenderableWidget(previousButton);

            var nextButton = new IndustrialButton(
                    panel.left() + panel.width() - 38,
                    layout.navigationTop(),
                    30,
                    NavigationLayout.NAVIGATION_HEIGHT,
                    Component.literal(">"),
                    ignored -> changePage(1));
            nextButton.active = scrollRow < layout.maximumScrollRow();
            nextButton.setTooltip(Tooltip.create(Component.literal("下一页")));
            addRenderableWidget(nextButton);
            return;
        }
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

    private PanelBounds panelBounds() {
        int contentBottom = layout.returnTop() + NavigationLayout.RETURN_HEIGHT;
        int left = Math.max(6, layout.gridLeft() - 10);
        int top = Math.max(4, layout.titleTop() - 8);
        int right = Math.min(width - 6,
                layout.gridLeft() + layout.gridWidth() + 10);
        int bottom = Math.min(height - 4, contentBottom + 10);
        return new PanelBounds(left, top, right - left, bottom - top);
    }

    private static ItemStack actionIcon(String actionId) {
        return switch (actionId) {
            case "balance" -> new ItemStack(Items.GOLD_INGOT);
            case "shop" -> new ItemStack(Items.HOPPER);
            case "sell" -> new ItemStack(Items.EMERALD);
            case "market" -> new ItemStack(Items.CHEST);
            case "settings" -> new ItemStack(Items.COMPARATOR);
            case "team" -> new ItemStack(Items.PLAYER_HEAD);
            default -> ItemStack.EMPTY;
        };
    }

    private record ActionView(
            String actionId,
            MenuActions.Definition definition) {
    }

    private record PanelBounds(int left, int top, int width, int height) {
    }

}

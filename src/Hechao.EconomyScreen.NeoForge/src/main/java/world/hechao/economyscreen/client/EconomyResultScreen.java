package world.hechao.economyscreen.client;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.locale.Language;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

final class EconomyResultScreen extends SinglePassBackgroundScreen {
    private static final int BUTTON_GAP = 8;
    private static final int ICON_DOCK_SIZE = 42;
    private static final int ICON_SIZE = 16;

    private final EconomyResultState state;
    private Button confirmButton;
    private Button closeButton;
    private EconomyResultLayout.Layout layout;
    private int animationTick;

    EconomyResultScreen(
            String actionId,
            Component title,
            String loadingMessage) {
        super(title);
        state = new EconomyResultState(actionId, loadingMessage);
    }

    @Override
    protected void init() {
        layout = EconomyResultLayout.calculate(width, height);
        confirmButton = new IndustrialButton(
                layout.panelLeft() + 12,
                layout.buttonY(),
                layout.buttonWidth(),
                EconomyResultLayout.BUTTON_HEIGHT,
                Component.literal("确认出售"),
                ignored -> confirmSale());
        addRenderableWidget(confirmButton);

        closeButton = new IndustrialButton(
                width / 2 - layout.buttonWidth() / 2,
                layout.buttonY(),
                layout.buttonWidth(),
                EconomyResultLayout.BUTTON_HEIGHT,
                Component.literal("返回首页"),
                ignored -> ClientEconomyUiBridge.requestHome());
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
        animationTick++;
        syncButtons();
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        var presentation = EconomyResultPresentation.from(state);
        int accent = accent(presentation.kind());

        IndustrialUiTheme.renderPanel(
                graphics,
                layout.panelLeft(),
                layout.panelTop(),
                layout.panelWidth(),
                layout.panelHeight());
        IndustrialUiTheme.renderEmblem(
                graphics,
                layout.panelLeft() + 10,
                layout.panelTop() + 6,
                22);
        graphics.drawString(
                font,
                Language.getInstance().getVisualOrder(font.ellipsize(
                        title,
                        Math.max(1, layout.panelWidth() - 118))),
                layout.panelLeft() + 39,
                layout.panelTop() + 12,
                0xFFFFFFFF,
                true);
        renderHeaderStatus(graphics, presentation, accent);

        if (layout.contentHeight() <= 8) {
            return;
        }
        IndustrialUiTheme.renderInstrumentBay(
                graphics,
                layout.contentLeft(),
                layout.contentTop(),
                layout.contentWidth(),
                layout.contentHeight(),
                accent);
        if (layout.detailed()) {
            renderDetailedResult(graphics, presentation, accent);
        } else {
            renderCompactResult(graphics, presentation, accent);
        }
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private void renderHeaderStatus(
            GuiGraphics graphics,
            EconomyResultPresentation.View presentation,
            int accent) {
        String label = switch (presentation.kind()) {
            case LOADING -> "同步中";
            case ERROR -> "异常";
            default -> "已响应";
        };
        int labelWidth = font.width(label);
        int lampX = layout.panelLeft() + layout.panelWidth() - labelWidth - 28;
        int lampY = layout.panelTop() + 13;
        IndustrialUiTheme.renderStatusLamp(
                graphics,
                lampX,
                lampY,
                accent,
                presentation.kind() != EconomyResultPresentation.Kind.LOADING
                        || (animationTick / 8) % 2 == 0);
        graphics.drawString(
                font,
                label,
                lampX + 11,
                layout.panelTop() + 12,
                0xFFC7CCCE,
                false);
    }

    private void renderDetailedResult(
            GuiGraphics graphics,
            EconomyResultPresentation.View presentation,
            int accent) {
        int bayLeft = layout.contentLeft();
        int bayTop = layout.contentTop();
        int dockLeft = bayLeft + 12;
        int dockTop = bayTop + Math.max(9, (layout.contentHeight() - ICON_DOCK_SIZE) / 2);
        IndustrialUiTheme.renderIconDock(
                graphics,
                dockLeft,
                dockTop,
                ICON_DOCK_SIZE,
                accent);
        graphics.renderItem(
                icon(presentation),
                dockLeft + (ICON_DOCK_SIZE - ICON_SIZE) / 2,
                dockTop + (ICON_DOCK_SIZE - ICON_SIZE) / 2);

        int textLeft = dockLeft + ICON_DOCK_SIZE + 13;
        int textRight = bayLeft + layout.contentWidth() - 12;
        int labelY = bayTop + 11;
        graphics.drawString(
                font,
                presentation.label(),
                textLeft,
                labelY,
                0xFFADB5B7,
                false);

        int primaryY = labelY + 13;
        float scale = presentation.hasMonetaryValue() ? 2.0F : 1.35F;
        int availablePrimaryWidth = Math.max(1, textRight - textLeft);
        if (!presentation.unit().isBlank()) {
            availablePrimaryWidth -= font.width(presentation.unit()) + 8;
        }
        scale = fitScale(presentation.primary(), scale, availablePrimaryWidth);
        drawScaledString(
                graphics,
                presentation.primary(),
                textLeft,
                primaryY,
                presentation.hasMonetaryValue() ? 0xFFFFD66B : 0xFFF2F4F4,
                scale);
        if (!presentation.unit().isBlank()) {
            int unitX = textLeft + Math.round(font.width(presentation.primary()) * scale) + 7;
            graphics.drawString(
                    font,
                    presentation.unit(),
                    unitX,
                    primaryY + Math.max(1, Math.round(8 * scale) - 8),
                    0xFFD6B86E,
                    false);
        }

        int secondaryY = primaryY + Math.max(17, Math.round(10 * scale) + 4);
        graphics.drawString(
                font,
                fit(presentation.secondary(), textRight - textLeft),
                textLeft,
                secondaryY,
                accent,
                false);

        int detailY = secondaryY + 13;
        if (!presentation.detail().isBlank()
                && detailY + 9 < bayTop + layout.contentHeight() - 6) {
            graphics.drawString(
                    font,
                    fit(presentation.detail(), textRight - textLeft),
                    textLeft,
                    detailY,
                    0xFF939C9F,
                    false);
        }

        int ticksY = bayTop + layout.contentHeight() - 8;
        int activeTicks = presentation.kind() == EconomyResultPresentation.Kind.LOADING
                ? 1 + (animationTick / 5) % 6
                : 6;
        IndustrialUiTheme.renderSignalTicks(
                graphics,
                textLeft,
                textRight,
                ticksY,
                accent,
                activeTicks);
    }

    private void renderCompactResult(
            GuiGraphics graphics,
            EconomyResultPresentation.View presentation,
            int accent) {
        int left = layout.contentLeft() + 10;
        int right = layout.contentLeft() + layout.contentWidth() - 10;
        int top = layout.contentTop() + 8;
        IndustrialUiTheme.renderStatusLamp(
                graphics,
                left,
                top,
                accent,
                true);
        graphics.drawString(
                font,
                fit(presentation.primary(), Math.max(1, right - left - 13)),
                left + 13,
                top - 1,
                presentation.hasMonetaryValue() ? 0xFFFFD66B : 0xFFF2F4F4,
                true);
        if (layout.contentHeight() >= 28) {
            graphics.drawString(
                    font,
                    fit(presentation.secondary(), Math.max(1, right - left)),
                    left,
                    top + 13,
                    0xFFADB5B7,
                    false);
        }
    }

    private ItemStack icon(EconomyResultPresentation.View presentation) {
        return switch (presentation.kind()) {
            case LOADING -> new ItemStack(Items.CLOCK);
            case BALANCE, SALE_SUCCESS -> new ItemStack(Items.GOLD_INGOT);
            case QUOTE -> itemIcon(presentation.itemId());
            case SUCCESS -> new ItemStack(Items.EMERALD);
            case ERROR -> new ItemStack(Items.BARRIER);
        };
    }

    private static ItemStack itemIcon(String itemId) {
        try {
            var location = ResourceLocation.tryParse(itemId);
            if (location == null) {
                return new ItemStack(Items.CHEST);
            }
            var item = BuiltInRegistries.ITEM.get(location);
            return item == Items.AIR
                    ? new ItemStack(Items.CHEST)
                    : new ItemStack(item);
        } catch (RuntimeException ignored) {
            return new ItemStack(Items.CHEST);
        }
    }

    private void drawScaledString(
            GuiGraphics graphics,
            String text,
            int x,
            int y,
            int color,
            float scale) {
        var pose = graphics.pose();
        pose.pushPose();
        pose.translate(x, y, 0);
        pose.scale(scale, scale, 1.0F);
        graphics.drawString(font, text, 0, 0, color, true);
        pose.popPose();
    }

    private float fitScale(String text, float preferred, int maximumWidth) {
        int textWidth = Math.max(1, font.width(text));
        return Math.max(1.0F, Math.min(preferred, maximumWidth / (float) textWidth));
    }

    private String fit(String text, int maximumWidth) {
        if (font.width(text) <= maximumWidth) {
            return text;
        }
        int suffixWidth = font.width("...");
        return font.plainSubstrByWidth(text, Math.max(0, maximumWidth - suffixWidth)) + "...";
    }

    private static int accent(EconomyResultPresentation.Kind kind) {
        return switch (kind) {
            case LOADING -> 0xFFFFD75A;
            case ERROR -> 0xFFFF8A80;
            default -> 0xFF8CD99B;
        };
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
                ? layout.panelLeft() + layout.panelWidth()
                        - 12 - layout.buttonWidth()
                : width / 2 - layout.buttonWidth() / 2);
        if (confirmButton.visible) {
            confirmButton.setX(
                    layout.panelLeft() + layout.panelWidth() / 2
                            - BUTTON_GAP / 2 - layout.buttonWidth());
        }
    }
}

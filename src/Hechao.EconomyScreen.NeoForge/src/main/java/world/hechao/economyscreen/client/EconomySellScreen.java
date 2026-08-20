package world.hechao.economyscreen.client;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.inventory.ContainerScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ClickType;

final class EconomySellScreen extends ContainerScreen {
    private static final int INPUT_SLOT = 13;
    private static final int STATUS_SLOT = 4;
    private static final int CONFIRM_SLOT = 22;
    private static final int RETURN_SLOT = 26;

    private Button confirmButton;
    private EconomySellLayout.Layout layout;

    EconomySellScreen(ChestMenu menu, Inventory playerInventory) {
        super(menu, playerInventory, Component.literal(ClientEconomyUiBridge.SELL_TITLE));
        inventoryLabelY = imageHeight - 94;
        titleLabelY = 7;
    }

    @Override
    protected void init() {
        super.init();
        layout = EconomySellLayout.calculate(
                width,
                height,
                leftPos,
                topPos,
                imageWidth,
                imageHeight);
        if (!layout.customControls()) {
            return;
        }
        confirmButton = new IndustrialButton(
                layout.confirmX(),
                layout.buttonY(),
                EconomySellLayout.BUTTON_WIDTH,
                EconomySellLayout.BUTTON_HEIGHT,
                Component.literal("确认出售"),
                ignored -> clickControl(CONFIRM_SLOT));
        confirmButton.setTooltip(Tooltip.create(Component.literal("报价完成后提交交易")));
        addRenderableWidget(confirmButton);

        var returnButton = new IndustrialButton(
                layout.returnX(),
                layout.buttonY(),
                EconomySellLayout.BUTTON_WIDTH,
                EconomySellLayout.BUTTON_HEIGHT,
                Component.literal("返回首页"),
                ignored -> returnHome());
        returnButton.setTooltip(Tooltip.create(Component.literal("取回未出售物品并返回")));
        addRenderableWidget(returnButton);
        syncButtons();
    }

    @Override
    public void render(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        syncButtons();
        super.render(graphics, mouseX, mouseY, partialTick);
        renderTooltip(graphics, mouseX, mouseY);
    }

    @Override
    protected void renderBg(
            GuiGraphics graphics,
            float partialTick,
            int mouseX,
            int mouseY) {
        IndustrialUiTheme.renderBackdrop(graphics, width, height);
        if (layout.expandedHeader()) {
            IndustrialUiTheme.renderPanel(
                    graphics,
                    layout.panelLeft(),
                    layout.panelTop(),
                    layout.panelWidth(),
                    layout.panelHeight());
        } else {
            renderCompactPanel(graphics);
        }
        renderTopArea(graphics);
        renderInventorySlots(graphics);
    }

    @Override
    protected void renderLabels(
            GuiGraphics graphics,
            int mouseX,
            int mouseY) {
        int titleY = layout.titleY() - topPos;
        IndustrialUiTheme.renderEmblem(graphics, 7, titleY - 4, 22);
        graphics.drawString(font, title, 35, titleY + 2, 0xFFFFFFFF, true);
        graphics.drawString(
                font,
                playerInventoryTitle,
                inventoryLabelX,
                inventoryLabelY,
                0xFFB7BBC0,
                false);

        var status = menu.getSlot(STATUS_SLOT).getItem();
        String heading = status.isEmpty()
                ? "等待放入物品"
                : status.getHoverName().getString();
        if (layout.statusY() >= 0) {
            graphics.drawCenteredString(
                    font,
                    fit(heading, imageWidth - 18),
                    imageWidth / 2,
                    layout.statusY() - topPos,
                    0xFFFFD75A);
        }
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private void renderTopArea(GuiGraphics graphics) {
        int inputX = leftPos + menu.getSlot(INPUT_SLOT).x;
        int inputY = topPos + menu.getSlot(INPUT_SLOT).y;
        IndustrialUiTheme.renderIconDock(graphics, inputX - 7, inputY - 7, 30, 0xFFE2B95F);
        graphics.renderOutline(inputX - 1, inputY - 1, 18, 18, 0xFFFFD75A);
        IndustrialUiTheme.renderDivider(
                graphics,
                leftPos + 8,
                leftPos + imageWidth - 8,
                topPos + 82);
    }

    private void renderInventorySlots(GuiGraphics graphics) {
        for (int index = 27; index < menu.slots.size(); index++) {
            var slot = menu.getSlot(index);
            int x = leftPos + slot.x - 1;
            int y = topPos + slot.y - 1;
            graphics.fill(x, y, x + 18, y + 18, 0xA5121719);
            graphics.renderOutline(x, y, 18, 18, 0xFF465154);
        }
    }

    private void renderCompactPanel(GuiGraphics graphics) {
        int left = layout.panelLeft();
        int top = layout.panelTop();
        int right = left + layout.panelWidth();
        int bottom = top + layout.panelHeight();
        graphics.fill(left + 3, top + 3, right + 3, bottom + 3, 0x8A000000);
        graphics.fill(left, top, right, bottom, 0xF21A1E20);
        graphics.renderOutline(left, top, layout.panelWidth(), layout.panelHeight(), 0xFF9E793E);
        graphics.renderOutline(
                left + 2,
                top + 2,
                layout.panelWidth() - 4,
                layout.panelHeight() - 4,
                0xFF3E474A);
    }

    private void syncButtons() {
        if (confirmButton == null || CONFIRM_SLOT >= menu.slots.size()) {
            return;
        }
        var control = menu.getSlot(CONFIRM_SLOT).getItem();
        confirmButton.active = !control.isEmpty()
                && "确认出售".equals(control.getHoverName().getString());
    }

    private void clickControl(int slot) {
        if (minecraft == null || minecraft.player == null || minecraft.gameMode == null) {
            return;
        }
        minecraft.gameMode.handleInventoryMouseClick(
                menu.containerId,
                slot,
                0,
                ClickType.PICKUP,
                minecraft.player);
    }

    private void returnHome() {
        ClientEconomyUiBridge.requestHome();
    }

    private String fit(String text, int maximumWidth) {
        if (font.width(text) <= maximumWidth) {
            return text;
        }
        return font.plainSubstrByWidth(text, Math.max(0, maximumWidth - font.width("...")))
                + "...";
    }
}

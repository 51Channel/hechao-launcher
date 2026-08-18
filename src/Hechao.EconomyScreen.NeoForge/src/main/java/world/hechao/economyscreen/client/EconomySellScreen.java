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
    private static final int BUTTON_WIDTH = 82;
    private static final int BUTTON_HEIGHT = 20;

    private Button confirmButton;

    EconomySellScreen(ChestMenu menu, Inventory playerInventory) {
        super(menu, playerInventory, Component.literal(ClientEconomyUiBridge.SELL_TITLE));
        inventoryLabelY = imageHeight - 94;
        titleLabelY = 7;
    }

    @Override
    protected void init() {
        super.init();
        int buttonY = topPos + 58;
        confirmButton = new IndustrialButton(
                leftPos + 5,
                buttonY,
                BUTTON_WIDTH,
                BUTTON_HEIGHT,
                Component.literal("确认出售"),
                ignored -> clickControl(CONFIRM_SLOT));
        confirmButton.setTooltip(Tooltip.create(Component.literal("报价完成后提交交易")));
        addRenderableWidget(confirmButton);

        var returnButton = new IndustrialButton(
                leftPos + imageWidth - BUTTON_WIDTH - 5,
                buttonY,
                BUTTON_WIDTH,
                BUTTON_HEIGHT,
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
        IndustrialUiTheme.renderPanel(
                graphics,
                leftPos - 4,
                topPos - 4,
                imageWidth + 8,
                imageHeight + 8);
        renderTopArea(graphics);
        renderInventorySlots(graphics);
    }

    @Override
    protected void renderLabels(
            GuiGraphics graphics,
            int mouseX,
            int mouseY) {
        IndustrialUiTheme.renderEmblem(graphics, 7, 3, 22);
        graphics.drawString(font, title, 35, 10, 0xFFFFFFFF, true);
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
        graphics.drawCenteredString(font, fit(heading, 124), imageWidth / 2, 31, 0xFFFFD75A);
        graphics.drawCenteredString(
                font,
                "把一组普通物品放入中央槽位",
                imageWidth / 2,
                45,
                0xFFADB5B7);
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
        clickControl(RETURN_SLOT);
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

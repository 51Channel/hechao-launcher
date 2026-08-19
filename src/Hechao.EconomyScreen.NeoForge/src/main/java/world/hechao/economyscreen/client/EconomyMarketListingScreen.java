package world.hechao.economyscreen.client;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.inventory.ContainerScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ClickType;
import net.minecraft.world.inventory.Slot;
import net.minecraft.world.item.Items;

final class EconomyMarketListingScreen extends ContainerScreen {
    private static final int CUSTOM_IMAGE_WIDTH = 278;
    private static final int INPUT_SLOT = 13;
    private static final int STATUS_SLOT = 4;
    private static final int CONFIRM_SLOT = 22;
    private static final int RETURN_SLOT = 26;
    private static final int PRICE_LEFT = 178;
    private static final int PRICE_TOP = 40;
    private static final int PRICE_WIDTH = 88;

    private EditBox priceBox;
    private Button confirmButton;
    private String priceText = "";
    private int submitCooldown;

    EconomyMarketListingScreen(ChestMenu menu, Inventory playerInventory) {
        super(menu, playerInventory, Component.literal(ClientEconomyUiBridge.MARKET_LISTING_TITLE));
        imageWidth = CUSTOM_IMAGE_WIDTH;
        inventoryLabelY = imageHeight - 94;
        titleLabelY = 7;
    }

    @Override
    protected void init() {
        super.init();
        priceBox = new EditBox(
                font,
                leftPos + PRICE_LEFT + 5,
                topPos + PRICE_TOP + 2,
                PRICE_WIDTH - 10,
                16,
                Component.literal("挂单总价"));
        priceBox.setBordered(false);
        priceBox.setMaxLength(13);
        priceBox.setFilter(MarketPriceInput::accepts);
        priceBox.setHint(Component.literal("总价，最低 1.00"));
        priceBox.setValue(priceText);
        priceBox.setResponder(value -> {
            priceText = value;
            syncButtons();
        });
        addRenderableWidget(priceBox);

        confirmButton = new IndustrialButton(
                leftPos + PRICE_LEFT,
                topPos + 61,
                PRICE_WIDTH,
                20,
                Component.literal("确认上架"),
                ignored -> submitListing());
        confirmButton.setTooltip(Tooltip.create(Component.literal("提交挂单并支付上架费")));
        addRenderableWidget(confirmButton);

        var returnButton = new IndustrialButton(
                leftPos + imageWidth - 29,
                topPos + 6,
                20,
                20,
                Component.literal("<"),
                ignored -> clickControl(RETURN_SLOT));
        returnButton.setTooltip(Tooltip.create(Component.literal("取回物品并返回玩家市场")));
        addRenderableWidget(returnButton);
        syncButtons();
    }

    @Override
    protected void containerTick() {
        super.containerTick();
        if (submitCooldown > 0) {
            submitCooldown--;
        }
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
        if (hoveredSlot == null || visibleSlot(hoveredSlot)) {
            renderTooltip(graphics, mouseX, mouseY);
        }
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
        int inputX = leftPos + menu.getSlot(INPUT_SLOT).x;
        int inputY = topPos + menu.getSlot(INPUT_SLOT).y;
        IndustrialUiTheme.renderIconDock(graphics, inputX - 7, inputY - 7, 30, 0xFFE2B95F);
        graphics.renderOutline(inputX - 1, inputY - 1, 18, 18, 0xFFFFD75A);
        IndustrialUiTheme.renderInstrumentBay(
                graphics,
                leftPos + PRICE_LEFT - 2,
                topPos + 33,
                PRICE_WIDTH + 4,
                50,
                0xFFE2B95F);
        IndustrialUiTheme.renderInputField(
                graphics,
                priceBox.getX() - 5,
                priceBox.getY() - 1,
                priceBox.getWidth() + 10,
                18,
                priceBox.isFocused());
        IndustrialUiTheme.renderDivider(
                graphics,
                leftPos + 8,
                leftPos + imageWidth - 8,
                topPos + 84);
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
        String statusText = status.isEmpty()
                ? "放入一组普通物品"
                : status.getHoverName().getString();
        var input = menu.getSlot(INPUT_SLOT).getItem();
        String itemName = input.isEmpty()
                ? "等待放入物品"
                : input.getHoverName().getString() + " × " + input.getCount();
        int statusColor = status.is(Items.REDSTONE) ? 0xFFFF8A80 : 0xFFFFD75A;

        graphics.drawString(font, "上架物品", 12, 39, 0xFFADB5B7, false);
        graphics.drawString(font, fit(itemName, 66), 106, 39, 0xFFFFFFFF, false);
        graphics.drawString(font, fit(statusText, 156), 12, 62, statusColor, false);
        graphics.drawString(font, "挂单总价", PRICE_LEFT + 7, 36, 0xFFADB5B7, false);
        graphics.drawString(
                font,
                fit("上架规则：最低 1.00 · 手续费 1%", 166),
                12,
                75,
                0xFF8CD99B,
                false);
    }

    @Override
    protected void renderSlot(GuiGraphics graphics, Slot slot) {
        if (visibleSlot(slot)) {
            super.renderSlot(graphics, slot);
        }
    }

    @Override
    protected void renderSlotHighlight(
            GuiGraphics graphics,
            Slot slot,
            int mouseX,
            int mouseY,
            float partialTick) {
        if (visibleSlot(slot)) {
            super.renderSlotHighlight(graphics, slot, mouseX, mouseY, partialTick);
        }
    }

    @Override
    protected void slotClicked(Slot slot, int button, int clickType, ClickType type) {
        if (slot != null && visibleSlot(slot)) {
            super.slotClicked(slot, button, clickType, type);
        }
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private void submitListing() {
        var price = MarketPriceInput.parse(priceText);
        var connection = minecraft == null ? null : minecraft.getConnection();
        if (price == null || connection == null || submitCooldown > 0) {
            return;
        }
        submitCooldown = 10;
        connection.sendCommand("hechaoeconomy:ah list " + price.toPlainString());
        syncButtons();
    }

    private void syncButtons() {
        if (confirmButton == null || CONFIRM_SLOT >= menu.slots.size()) {
            return;
        }
        var control = menu.getSlot(CONFIRM_SLOT).getItem();
        boolean serverReady = !control.isEmpty()
                && "输入总价后确认".equals(control.getHoverName().getString());
        confirmButton.active = submitCooldown == 0
                && serverReady
                && MarketPriceInput.parse(priceText) != null;
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

    private void renderInventorySlots(GuiGraphics graphics) {
        for (int index = 27; index < menu.slots.size(); index++) {
            var slot = menu.getSlot(index);
            int x = leftPos + slot.x - 1;
            int y = topPos + slot.y - 1;
            graphics.fill(x, y, x + 18, y + 18, 0xA5121719);
            graphics.renderOutline(x, y, 18, 18, 0xFF465154);
        }
    }

    private static boolean visibleSlot(Slot slot) {
        return slot.index == INPUT_SLOT || slot.index >= 27;
    }

    private String fit(String text, int maximumWidth) {
        if (font.width(text) <= maximumWidth) {
            return text;
        }
        return font.plainSubstrByWidth(text, Math.max(0, maximumWidth - font.width("...")))
                + "...";
    }
}

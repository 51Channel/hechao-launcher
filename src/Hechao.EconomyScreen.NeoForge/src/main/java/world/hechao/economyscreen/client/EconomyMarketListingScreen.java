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
    private static final int LISTING_HEADER_HEIGHT = 28;
    private static final int ITEM_MODULE_LEFT = 10;
    private static final int ITEM_MODULE_TOP = 31;
    private static final int ITEM_MODULE_WIDTH = 146;
    private static final int ITEM_MODULE_HEIGHT = 46;
    private static final int PRICE_MODULE_LEFT = 162;
    private static final int PRICE_MODULE_TOP = 31;
    private static final int PRICE_MODULE_WIDTH = 106;
    private static final int PRICE_MODULE_HEIGHT = 49;
    private static final int PRICE_LEFT = 168;
    private static final int PRICE_TOP = 43;
    private static final int PRICE_WIDTH = 94;
    private static final int CONFIRM_TOP = 61;
    private static final int INVENTORY_MODULE_LEFT = 5;
    private static final int INVENTORY_MODULE_TOP = 80;
    private static final int INVENTORY_MODULE_WIDTH = 170;
    private static final int INVENTORY_MODULE_HEIGHT = 85;
    private static final int GUIDE_MODULE_LEFT = 181;
    private static final int GUIDE_MODULE_TOP = 80;
    private static final int GUIDE_MODULE_WIDTH = 87;
    private static final int GUIDE_MODULE_HEIGHT = 85;

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
                topPos + CONFIRM_TOP,
                PRICE_WIDTH,
                18,
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
                imageHeight + 8,
                LISTING_HEADER_HEIGHT);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + ITEM_MODULE_LEFT,
                topPos + ITEM_MODULE_TOP,
                ITEM_MODULE_WIDTH,
                ITEM_MODULE_HEIGHT,
                0xFFE2B95F);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + PRICE_MODULE_LEFT,
                topPos + PRICE_MODULE_TOP,
                PRICE_MODULE_WIDTH,
                PRICE_MODULE_HEIGHT,
                0xFF6DAEA8);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + INVENTORY_MODULE_LEFT,
                topPos + INVENTORY_MODULE_TOP,
                INVENTORY_MODULE_WIDTH,
                INVENTORY_MODULE_HEIGHT,
                0xFF3E7C79);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + GUIDE_MODULE_LEFT,
                topPos + GUIDE_MODULE_TOP,
                GUIDE_MODULE_WIDTH,
                GUIDE_MODULE_HEIGHT,
                0xFF6DAEA8);
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

        graphics.drawString(font, "物品槽", ITEM_MODULE_LEFT + 8, ITEM_MODULE_TOP + 10,
                0xFFADB5B7, false);
        graphics.drawString(font, fit(itemName, 56), ITEM_MODULE_LEFT + 8,
                ITEM_MODULE_TOP + 28, 0xFFFFFFFF, false);
        graphics.drawString(font, "定价与操作", PRICE_MODULE_LEFT + 8,
                PRICE_MODULE_TOP + 10, 0xFFADB5B7, false);
        graphics.drawString(font, "上架说明", GUIDE_MODULE_LEFT + 8, GUIDE_MODULE_TOP + 10,
                0xFFADB5B7, false);
        graphics.drawString(font, fit(statusText, GUIDE_MODULE_WIDTH - 16),
                GUIDE_MODULE_LEFT + 8, GUIDE_MODULE_TOP + 27, statusColor, false);
        graphics.drawString(font, "最低 1.00", GUIDE_MODULE_LEFT + 8,
                GUIDE_MODULE_TOP + 43, 0xFF8CD99B, false);
        graphics.drawString(font, "手续费 1%", GUIDE_MODULE_LEFT + 8,
                GUIDE_MODULE_TOP + 57, 0xFF8CD99B, false);
        graphics.drawString(font, "放入后定价", GUIDE_MODULE_LEFT + 8,
                GUIDE_MODULE_TOP + 71, 0xFF8D9799, false);
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

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
    private static final int INPUT_SLOT = 13;
    private static final int STATUS_SLOT = 4;
    private static final int CONFIRM_SLOT = 22;
    private static final int RETURN_SLOT = 26;
    private static final int LISTING_HEADER_HEIGHT = 28;

    private EditBox priceBox;
    private Button confirmButton;
    private String priceText = "";
    private int submitCooldown;
    private EconomyMarketListingLayout.Layout layout;

    EconomyMarketListingScreen(ChestMenu menu, Inventory playerInventory) {
        super(menu, playerInventory, Component.literal(ClientEconomyUiBridge.MARKET_LISTING_TITLE));
        imageWidth = EconomyMarketListingLayout.IMAGE_WIDTH;
        inventoryLabelY = EconomyMarketListingLayout.IMAGE_HEIGHT - 96;
        titleLabelY = 7;
    }

    @Override
    protected void init() {
        super.init();
        layout = EconomyMarketListingLayout.calculate(
                imageWidth,
                imageHeight,
                menu.getSlot(INPUT_SLOT).x,
                menu.getSlot(INPUT_SLOT).y);
        var priceField = layout.priceField();
        priceBox = new EditBox(
                font,
                leftPos + priceField.left() + 4,
                topPos + priceField.top() + 2,
                priceField.width() - 8,
                priceField.height() - 4,
                Component.literal("挂单总价"));
        priceBox.setBordered(false);
        priceBox.setMaxLength(13);
        priceBox.setFilter(MarketPriceInput::accepts);
        priceBox.setHint(Component.literal("最低1.00"));
        priceBox.setValue(priceText);
        priceBox.setResponder(value -> {
            priceText = value;
            syncButtons();
        });
        addRenderableWidget(priceBox);

        var confirmBounds = layout.confirmButton();
        confirmButton = new IndustrialButton(
                leftPos + confirmBounds.left(),
                topPos + confirmBounds.top(),
                confirmBounds.width(),
                confirmBounds.height(),
                Component.literal("上架"),
                ignored -> submitListing());
        confirmButton.setTooltip(Tooltip.create(Component.literal("提交挂单并支付上架费")));
        addRenderableWidget(confirmButton);

        var returnBounds = layout.returnButton();
        var returnButton = new IndustrialButton(
                leftPos + returnBounds.left(),
                topPos + returnBounds.top(),
                returnBounds.width(),
                returnBounds.height(),
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
                leftPos + layout.panel().left(),
                topPos + layout.panel().top(),
                layout.panel().width(),
                layout.panel().height(),
                LISTING_HEADER_HEIGHT);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + layout.itemModule().left(),
                topPos + layout.itemModule().top(),
                layout.itemModule().width(),
                layout.itemModule().height(),
                0xFFE2B95F);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + layout.priceModule().left(),
                topPos + layout.priceModule().top(),
                layout.priceModule().width(),
                layout.priceModule().height(),
                0xFF6DAEA8);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + layout.inventoryModule().left(),
                topPos + layout.inventoryModule().top(),
                layout.inventoryModule().width(),
                layout.inventoryModule().height(),
                0xFF3E7C79);
        IndustrialUiTheme.renderModule(
                graphics,
                leftPos + layout.guideModule().left(),
                topPos + layout.guideModule().top(),
                layout.guideModule().width(),
                layout.guideModule().height(),
                0xFF6DAEA8);
        int inputX = leftPos + menu.getSlot(INPUT_SLOT).x;
        int inputY = topPos + menu.getSlot(INPUT_SLOT).y;
        IndustrialUiTheme.renderIconDock(
                graphics,
                leftPos + layout.inputDock().left(),
                topPos + layout.inputDock().top(),
                layout.inputDock().width(),
                0xFFE2B95F);
        graphics.renderOutline(inputX - 1, inputY - 1, 18, 18, 0xFFFFD75A);
        var field = layout.priceField();
        IndustrialUiTheme.renderInputField(
                graphics,
                leftPos + field.left(),
                topPos + field.top(),
                field.width(),
                field.height(),
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
                layout.inventoryLabelY(),
                0xFFB7BBC0,
                false);

        var status = menu.getSlot(STATUS_SLOT).getItem();
        String statusText = compactStatus(status.isEmpty()
                ? "放入一组普通物品"
                : status.getHoverName().getString());
        var input = menu.getSlot(INPUT_SLOT).getItem();
        String itemName = input.isEmpty()
                ? "等待放入物品"
                : input.getHoverName().getString() + " × " + input.getCount();
        int statusColor = status.is(Items.REDSTONE) ? 0xFFFF8A80 : 0xFFFFD75A;

        graphics.drawString(font, "物品槽", layout.itemLabelX(), layout.itemLabelY(),
                0xFFADB5B7, false);
        graphics.drawString(font, fit(itemName, layout.itemTextWidth()), layout.itemLabelX(),
                layout.itemNameY(), 0xFFFFFFFF, false);
        graphics.drawString(font, "定价与操作", layout.priceLabelX(), layout.priceLabelY(),
                0xFFADB5B7, false);
        graphics.drawString(font, "上架说明", layout.guideLabelX(), layout.guideLabelY(),
                0xFFADB5B7, false);
        graphics.drawString(font, fit(statusText, layout.guideModule().width() - 16),
                layout.guideLabelX(), layout.guideStatusY(), statusColor, false);
        graphics.drawString(font, "最低 1.00", layout.guideLabelX(),
                layout.guideMinimumY(), 0xFF8CD99B, false);
        graphics.drawString(font, "手续费 1%", layout.guideLabelX(),
                layout.guideFeeY(), 0xFF8CD99B, false);
        graphics.drawString(font, "放入后定价", layout.guideLabelX(),
                layout.guidePromptY(), 0xFF8D9799, false);
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

    private static String compactStatus(String statusText) {
        return switch (statusText) {
            case "放入要上架的普通物品" -> "等待放入物品";
            case "正在提交挂单" -> "提交中";
            case "上架结果暂时无法确认" -> "待确认结果";
            default -> statusText;
        };
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

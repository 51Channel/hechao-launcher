package world.hechao.economyscreen.client;

import java.util.List;
import net.minecraft.ChatFormatting;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ClickType;
import net.minecraft.world.item.ItemStack;

final class EconomyShopPurchaseScreen extends SinglePassBackgroundScreen {
    private static final int ITEM_SLOT = 13;
    private static final int MINUS_LARGE_SLOT = 10;
    private static final int MINUS_ONE_SLOT = 11;
    private static final int QUANTITY_SLOT = 12;
    private static final int PLUS_ONE_SLOT = 15;
    private static final int PLUS_LARGE_SLOT = 16;
    private static final int CONFIRM_SLOT = 22;
    private static final int RETURN_SLOT = 26;

    private final ChestMenu menu;
    private Button minusLarge;
    private Button minusOne;
    private Button plusOne;
    private Button plusLarge;
    private Button confirm;
    private Button back;
    private int panelLeft;
    private int panelTop;
    private int panelWidth;
    private int panelHeight;
    private boolean hovered;

    EconomyShopPurchaseScreen(ChestMenu menu) {
        super(Component.literal(ClientEconomyUiBridge.SHOP_PURCHASE_TITLE));
        this.menu = menu;
    }

    @Override
    protected void init() {
        panelWidth = Math.min(320, width - 8);
        panelHeight = Math.min(198, height - 4);
        panelLeft = (width - panelWidth) / 2;
        panelTop = (height - panelHeight) / 2;
        int buttonY = panelTop + panelHeight - 28;
        int buttonWidth = Math.max(42, Math.min(64, (panelWidth - 72) / 5));
        int gap = 4;
        int controlsLeft = panelLeft + (panelWidth - (buttonWidth * 4 + gap * 3)) / 2;
        minusLarge = addControl(controlsLeft, buttonY - 28, buttonWidth, "-64", MINUS_LARGE_SLOT);
        minusOne = addControl(controlsLeft + buttonWidth + gap,
                buttonY - 28, buttonWidth, "-1", MINUS_ONE_SLOT);
        plusOne = addControl(controlsLeft + (buttonWidth + gap) * 2,
                buttonY - 28, buttonWidth, "+1", PLUS_ONE_SLOT);
        plusLarge = addControl(controlsLeft + (buttonWidth + gap) * 3,
                buttonY - 28, buttonWidth, "+64", PLUS_LARGE_SLOT);
        int actionWidth = Math.min(108, Math.max(54, (panelWidth - 36) / 2));
        confirm = new IndustrialButton(
                panelLeft + 12,
                buttonY,
                actionWidth,
                20,
                Component.literal("确认购买"),
                ignored -> clickControl(CONFIRM_SLOT));
        confirm.setTooltip(Tooltip.create(Component.literal("扣除金币并生成待领取物品")));
        addRenderableWidget(confirm);
        back = new IndustrialButton(
                panelLeft + panelWidth - actionWidth - 12,
                buttonY,
                actionWidth,
                20,
                Component.literal("返回商城"),
                ignored -> clickControl(RETURN_SLOT));
        back.setTooltip(Tooltip.create(Component.literal("取消购买")));
        addRenderableWidget(back);
        syncButtons();
    }

    @Override
    public void tick() {
        super.tick();
        syncButtons();
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        IndustrialUiTheme.renderPanel(
                graphics,
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight);
        IndustrialUiTheme.renderEmblem(graphics, panelLeft + 10, panelTop + 6, 22);
        graphics.drawString(
                font,
                title,
                panelLeft + 39,
                panelTop + 11,
                0xFFFFFFFF,
                true);

        var item = item();
        int itemX = panelLeft + 22;
        int itemY = panelTop + 54;
        IndustrialUiTheme.renderIconDock(graphics, itemX - 7, itemY - 7, 38, 0xFFE2B95F);
        if (!item.isEmpty()) {
            graphics.renderItem(item, itemX + 3, itemY + 3);
        }
        hovered = mouseX >= itemX - 7
                && mouseX < itemX + 31
                && mouseY >= itemY - 7
                && mouseY < itemY + 31;

        String itemName = item.isEmpty() ? "商品正在同步" : item.getHoverName().getString();
        graphics.drawString(
                font,
                fit(itemName, panelWidth - 88),
                panelLeft + 72,
                panelTop + 45,
                0xFFFFFFFF,
                false);
        var details = lore(item);
        int detailY = panelTop + 62;
        for (int index = 0; index < Math.min(4, details.size()); index++) {
            graphics.drawString(
                    font,
                    fit(details.get(index), panelWidth - 88),
                    panelLeft + 72,
                    detailY + index * 14,
                    index == 0 ? 0xFFFFD75A : 0xFFADB5B7,
                    false);
        }
        IndustrialUiTheme.renderDivider(
                graphics,
                panelLeft + 8,
                panelLeft + panelWidth - 8,
                panelTop + panelHeight - 39);
        graphics.drawCenteredString(
                font,
                quantityLabel(),
                width / 2,
                panelTop + panelHeight - 49,
                0xFFB7BBC0);
    }

    @Override
    protected void renderOverlay(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        if (hovered && !item().isEmpty()) {
            graphics.renderTooltip(font, item(), mouseX, mouseY);
        }
    }

    @Override
    public void onClose() {
        if (minecraft != null
                && minecraft.player != null
                && minecraft.player.containerMenu == menu) {
            minecraft.player.closeContainer();
        }
        super.onClose();
    }

    @Override
    public boolean isPauseScreen() {
        return false;
    }

    private Button addControl(
            int x,
            int y,
            int width,
            String label,
            int slot) {
        var button = new IndustrialButton(
                x,
                y,
                width,
                20,
                Component.literal(label),
                ignored -> clickControl(slot));
        button.setTooltip(Tooltip.create(Component.literal("调整购买数量")));
        addRenderableWidget(button);
        return button;
    }

    private void syncButtons() {
        setActive(minusLarge, MINUS_LARGE_SLOT);
        setActive(minusOne, MINUS_ONE_SLOT);
        setActive(plusOne, PLUS_ONE_SLOT);
        setActive(plusLarge, PLUS_LARGE_SLOT);
        setActive(back, RETURN_SLOT);
        setActive(confirm, CONFIRM_SLOT);
    }

    private void setActive(Button button, int slot) {
        if (button == null || slot >= menu.slots.size()) {
            return;
        }
        button.active = !menu.getSlot(slot).getItem().isEmpty();
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

    private ItemStack item() {
        return ITEM_SLOT < menu.slots.size()
                ? menu.getSlot(ITEM_SLOT).getItem()
                : ItemStack.EMPTY;
    }

    private String quantityLabel() {
        if (QUANTITY_SLOT >= menu.slots.size()) {
            return "数量正在同步";
        }
        return menu.getSlot(QUANTITY_SLOT).getItem().getHoverName().getString();
    }

    private List<String> lore(ItemStack stack) {
        var lore = stack.get(DataComponents.LORE);
        if (lore == null) {
            return List.of("价格正在同步", "服务器会再次校验余额");
        }
        return lore.lines().stream()
                .map(Component::getString)
                .map(value -> value.replace(ChatFormatting.RESET.toString(), ""))
                .toList();
    }

    private String fit(String value, int maximumWidth) {
        if (font.width(value) <= maximumWidth) {
            return value;
        }
        return font.plainSubstrByWidth(
                value,
                Math.max(0, maximumWidth - font.width("..."))) + "...";
    }
}

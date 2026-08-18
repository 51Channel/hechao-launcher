package world.hechao.economyscreen.client;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ClickType;
import net.minecraft.world.item.ItemStack;

final class EconomyMarketDecisionScreen extends SinglePassBackgroundScreen {
    private static final int ITEM_SLOT = 13;
    private static final int CONFIRM_SLOT = 22;
    private static final int RETURN_SLOT = 26;

    private final ChestMenu menu;
    private final String serverTitle;
    private Button confirmButton;
    private int panelLeft;
    private int panelTop;
    private int panelWidth;
    private int panelHeight;
    private boolean itemHovered;

    EconomyMarketDecisionScreen(ChestMenu menu, String serverTitle) {
        super(Component.literal(serverTitle));
        this.menu = menu;
        this.serverTitle = serverTitle;
    }

    @Override
    protected void init() {
        panelWidth = Math.min(270, width - 8);
        panelHeight = Math.min(168, height - 4);
        panelLeft = (width - panelWidth) / 2;
        panelTop = (height - panelHeight) / 2;
        int buttonY = panelTop + panelHeight - 27;
        boolean cancelling = cancelling();
        int buttonWidth = Math.min(92, Math.max(50, (panelWidth - 36) / 2));
        boolean compactButtons = buttonWidth < 72;
        confirmButton = new IndustrialButton(
                panelLeft + 12,
                buttonY,
                buttonWidth,
                20,
                Component.literal(cancelling
                        ? compactButtons ? "下架" : "确认下架"
                        : compactButtons ? "购买" : "确认购买"),
                ignored -> clickControl(CONFIRM_SLOT));
        confirmButton.setTooltip(Tooltip.create(Component.literal(
                cancelling ? "物品会进入待领取" : "确认支付并购买")));
        addRenderableWidget(confirmButton);

        var returnButton = new IndustrialButton(
                panelLeft + panelWidth - buttonWidth - 12,
                buttonY,
                buttonWidth,
                20,
                Component.literal(compactButtons ? "返回" : "返回列表"),
                ignored -> clickControl(RETURN_SLOT));
        returnButton.setTooltip(Tooltip.create(Component.literal("取消本次操作")));
        addRenderableWidget(returnButton);
        syncConfirmButton();
    }

    @Override
    public void tick() {
        super.tick();
        syncConfirmButton();
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        syncConfirmButton();
        IndustrialUiTheme.renderPanel(
                graphics,
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight);
        IndustrialUiTheme.renderEmblem(graphics, panelLeft + 10, panelTop + 6, 22);
        graphics.drawString(font, title, panelLeft + 39, panelTop + 11, 0xFFFFFFFF, true);

        boolean compactHeight = panelHeight < 145;
        int itemX = panelLeft + 18;
        int itemY = panelTop + (compactHeight ? 48 : 55);
        IndustrialUiTheme.renderIconDock(graphics, itemX - 7, itemY - 7, 34, 0xFFE2B95F);
        var stack = item();
        if (!stack.isEmpty()) {
            graphics.renderItem(stack, itemX + 2, itemY + 2);
        }
        itemHovered = mouseX >= itemX - 7
                && mouseX < itemX + 27
                && mouseY >= itemY - 7
                && mouseY < itemY + 27;

        String heading = stack.isEmpty() ? "挂单信息正在同步" : stack.getHoverName().getString();
        graphics.drawString(
                font,
                fit(heading, panelWidth - 78),
                panelLeft + 62,
                panelTop + (compactHeight ? 43 : 51),
                0xFFFFFFFF,
                false);
        graphics.drawString(
                font,
                fit(primaryDetail(stack), panelWidth - 78),
                panelLeft + 62,
                panelTop + (compactHeight ? 58 : 66),
                0xFFFFD75A,
                false);
        if (!compactHeight) {
            graphics.drawString(
                    font,
                    cancelling()
                            ? "上架费不会退回，物品转入待领取"
                            : "购买后请前往待领取取出物品",
                    panelLeft + 18,
                    panelTop + 94,
                    0xFFADB5B7,
                    false);
        }
        IndustrialUiTheme.renderDivider(
                graphics,
                panelLeft + 8,
                panelLeft + panelWidth - 8,
                panelTop + panelHeight - 33);
    }

    @Override
    protected void renderOverlay(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        if (itemHovered && !item().isEmpty()) {
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

    private void syncConfirmButton() {
        if (confirmButton == null || CONFIRM_SLOT >= menu.slots.size()) {
            return;
        }
        var control = menu.getSlot(CONFIRM_SLOT).getItem();
        String expected = cancelling() ? "确认下架" : "确认购买";
        confirmButton.active = !control.isEmpty()
                && expected.equals(control.getHoverName().getString());
    }

    private ItemStack item() {
        return ITEM_SLOT < menu.slots.size()
                ? menu.getSlot(ITEM_SLOT).getItem()
                : ItemStack.EMPTY;
    }

    private String primaryDetail(ItemStack stack) {
        var lore = stack.get(DataComponents.LORE);
        return lore == null || lore.lines().isEmpty()
                ? "价格待同步"
                : lore.lines().getFirst().getString();
    }

    private boolean cancelling() {
        return ClientEconomyUiBridge.MARKET_CANCEL_TITLE.equals(serverTitle);
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

    private String fit(String text, int maximumWidth) {
        if (font.width(text) <= maximumWidth) {
            return text;
        }
        return font.plainSubstrByWidth(text, Math.max(0, maximumWidth - font.width("...")))
                + "...";
    }
}

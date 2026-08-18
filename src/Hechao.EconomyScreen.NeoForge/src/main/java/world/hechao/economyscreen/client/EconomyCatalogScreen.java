package world.hechao.economyscreen.client;

import java.util.ArrayList;
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

final class EconomyCatalogScreen extends SinglePassBackgroundScreen {
    private static final int PAGE_BUTTON_WIDTH = 30;
    private static final int CLOSE_BUTTON_WIDTH = 80;
    private static final int BUTTON_HEIGHT = 20;

    private final ChestMenu menu;
    private EconomyCatalogLayout.Layout layout;
    private Button previousButton;
    private Button nextButton;
    private ItemStack hovered = ItemStack.EMPTY;
    private int page;
    private int observedServerPage = -1;
    private boolean moveToLastPageAfterServerChange;

    EconomyCatalogScreen(ChestMenu menu) {
        super(Component.literal(ClientEconomyUiBridge.CATALOG_TITLE));
        this.menu = menu;
    }

    @Override
    protected void init() {
        layout = EconomyCatalogLayout.calculate(width, height);
        int buttonY = layout.footerTop() + 7;
        previousButton = new IndustrialButton(
                layout.panelLeft() + 12,
                buttonY,
                PAGE_BUTTON_WIDTH,
                BUTTON_HEIGHT,
                Component.literal("<"),
                ignored -> changePage(-1));
        previousButton.setTooltip(Tooltip.create(Component.literal("上一页")));
        addRenderableWidget(previousButton);

        nextButton = new IndustrialButton(
                layout.panelLeft() + layout.panelWidth()
                        - PAGE_BUTTON_WIDTH - 12,
                buttonY,
                PAGE_BUTTON_WIDTH,
                BUTTON_HEIGHT,
                Component.literal(">"),
                ignored -> changePage(1));
        nextButton.setTooltip(Tooltip.create(Component.literal("下一页")));
        addRenderableWidget(nextButton);

        addRenderableWidget(new IndustrialButton(
                width / 2 - CLOSE_BUTTON_WIDTH / 2,
                buttonY,
                CLOSE_BUTTON_WIDTH,
                BUTTON_HEIGHT,
                Component.literal("完成"),
                ignored -> onClose()));
        observedServerPage = serverPageInfo().page();
        syncNavigation(products().size());
    }

    @Override
    public void tick() {
        super.tick();
        var serverPage = serverPageInfo();
        if (serverPage.page() != observedServerPage) {
            observedServerPage = serverPage.page();
            if (moveToLastPageAfterServerChange) {
                page = maximumPage(products().size());
                moveToLastPageAfterServerChange = false;
            }
            syncNavigation(products().size());
        }
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        var products = products();
        syncNavigation(products.size());
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
                title,
                layout.panelLeft() + 39,
                layout.panelTop() + 11,
                0xFFFFFFFF,
                true);
        graphics.drawString(
                font,
                serverPageInfo().totalItemCount() + " 项",
                layout.panelLeft() + layout.panelWidth()
                        - 12 - font.width(serverPageInfo().totalItemCount() + " 项"),
                layout.panelTop() + 11,
                0xFFB7BBC0,
                false);

        hovered = ItemStack.EMPTY;
        if (products.isEmpty()) {
            int centerY = layout.contentTop()
                    + (layout.footerTop() - layout.contentTop()) / 2;
            graphics.drawCenteredString(
                    font,
                    "暂无已启用的回收商品",
                    width / 2,
                    centerY - 8,
                    0xFFFFFFFF);
            graphics.drawCenteredString(
                    font,
                    "商品开放后会自动显示在这里",
                    width / 2,
                    centerY + 8,
                    0xFF9FA4A9);
        } else {
            int first = page * layout.pageSize();
            int last = Math.min(products.size(), first + layout.pageSize());
            for (int index = first; index < last; index++) {
                int relative = index - first;
                int column = relative % layout.columns();
                int row = relative / layout.columns();
                int x = layout.contentLeft()
                        + column * (layout.cardWidth() + EconomyCatalogLayout.CARD_GAP);
                int y = layout.contentTop()
                        + row * (EconomyCatalogLayout.CARD_HEIGHT
                                + EconomyCatalogLayout.CARD_GAP);
                var product = products.get(index);
                boolean isHovered = mouseX >= x
                        && mouseX < x + layout.cardWidth()
                        && mouseY >= y
                        && mouseY < y + EconomyCatalogLayout.CARD_HEIGHT;
                IndustrialUiTheme.renderCard(
                        graphics,
                        x,
                        y,
                        layout.cardWidth(),
                        EconomyCatalogLayout.CARD_HEIGHT,
                        isHovered);
                graphics.renderItem(product, x + 8, y + 10);
                String name = displayName(product);
                graphics.drawString(
                        font,
                        fit(name, layout.cardWidth() - 38),
                        x + 30,
                        y + 7,
                        0xFFFFFFFF,
                        false);
                graphics.drawString(
                        font,
                        fit(price(product), layout.cardWidth() - 38),
                        x + 30,
                        y + 20,
                        0xFFFFD75A,
                        false);
                if (isHovered) {
                    hovered = product;
                }
            }
        }

        var serverPage = serverPageInfo();
        IndustrialUiTheme.renderDivider(
                graphics,
                layout.panelLeft() + 4,
                layout.panelLeft() + layout.panelWidth() - 4,
                layout.footerTop() - 1);
        if (maximumPage(products.size()) > 0 || serverPage.pageCount() > 1) {
            String indicator = serverPage.pageCount() > 1
                    ? "第 " + serverPage.page() + " / " + serverPage.pageCount()
                            + " 批 · 页 " + (page + 1) + " / "
                            + (maximumPage(products.size()) + 1)
                    : (page + 1) + " / " + (maximumPage(products.size()) + 1);
            graphics.drawCenteredString(
                    font,
                    indicator,
                    width / 2,
                    layout.footerTop() - 10,
                    0xFFB7BBC0);
        }
    }

    @Override
    protected void renderOverlay(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        if (!hovered.isEmpty()) {
            graphics.renderTooltip(font, hovered, mouseX, mouseY);
        }
    }

    @Override
    public boolean mouseScrolled(
            double mouseX,
            double mouseY,
            double scrollX,
            double scrollY) {
        if (scrollY == 0 || (!canMovePrevious() && !canMoveNext())) {
            return super.mouseScrolled(mouseX, mouseY, scrollX, scrollY);
        }
        changePage(scrollY > 0 ? -1 : 1);
        return true;
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

    private List<ItemStack> products() {
        var products = new ArrayList<ItemStack>();
        int productSlots = Math.min(
                EconomyCatalogServerPage.PRODUCT_SLOTS,
                menu.getRowCount() * 9);
        for (int index = 0; index < productSlots; index++) {
            var item = menu.getSlot(index).getItem();
            if (!item.isEmpty()) {
                products.add(item);
            }
        }
        return List.copyOf(products);
    }

    private void changePage(int direction) {
        int maximum = maximumPage(products().size());
        if (direction < 0) {
            if (page > 0) {
                page--;
            } else if (hasServerControl(
                    EconomyCatalogServerPage.PREVIOUS_SLOT,
                    EconomyCatalogServerPage.PREVIOUS_LABEL)) {
                moveToLastPageAfterServerChange = true;
                clickServerControl(EconomyCatalogServerPage.PREVIOUS_SLOT);
            }
        } else if (direction > 0) {
            if (page < maximum) {
                page++;
            } else if (hasServerControl(
                    EconomyCatalogServerPage.NEXT_SLOT,
                    EconomyCatalogServerPage.NEXT_LABEL)) {
                page = 0;
                moveToLastPageAfterServerChange = false;
                clickServerControl(EconomyCatalogServerPage.NEXT_SLOT);
            }
        }
        syncNavigation(products().size());
    }

    private void syncNavigation(int itemCount) {
        if (previousButton == null || nextButton == null) {
            return;
        }
        int maximum = maximumPage(itemCount);
        page = Math.min(page, maximum);
        boolean canPrevious = canMovePrevious();
        boolean canNext = canMoveNext();
        previousButton.visible = canPrevious || canNext;
        nextButton.visible = canPrevious || canNext;
        previousButton.active = canPrevious;
        nextButton.active = canNext;
    }

    private int maximumPage(int itemCount) {
        return EconomyCatalogLayout.maximumPage(itemCount, layout.pageSize());
    }

    private boolean canMovePrevious() {
        return page > 0 || hasServerControl(
                EconomyCatalogServerPage.PREVIOUS_SLOT,
                EconomyCatalogServerPage.PREVIOUS_LABEL);
    }

    private boolean canMoveNext() {
        return page < maximumPage(products().size()) || hasServerControl(
                EconomyCatalogServerPage.NEXT_SLOT,
                EconomyCatalogServerPage.NEXT_LABEL);
    }

    private boolean hasServerControl(int slot, String expectedLabel) {
        if (slot >= menu.slots.size()) {
            return false;
        }
        var item = menu.getSlot(slot).getItem();
        return !item.isEmpty() && expectedLabel.equals(item.getHoverName().getString());
    }

    private EconomyCatalogServerPage.Info serverPageInfo() {
        if (EconomyCatalogServerPage.PAGE_INFO_SLOT >= menu.slots.size()) {
            return EconomyCatalogServerPage.parse("", products().size());
        }
        var item = menu.getSlot(EconomyCatalogServerPage.PAGE_INFO_SLOT).getItem();
        String label = item.isEmpty() ? "" : item.getHoverName().getString();
        return EconomyCatalogServerPage.parse(label, products().size());
    }

    private void clickServerControl(int slot) {
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

    private String displayName(ItemStack stack) {
        String name = stack.getHoverName().getString();
        if (name.startsWith("item.minecraft.")
                || name.startsWith("block.minecraft.")) {
            return Component.translatable(stack.getDescriptionId()).getString();
        }
        return name;
    }

    private static String price(ItemStack stack) {
        var lore = stack.get(DataComponents.LORE);
        if (lore == null) {
            return "价格待同步";
        }
        return lore.lines().stream()
                .map(Component::getString)
                .filter(line -> line.startsWith("回收价:"))
                .findFirst()
                .orElse("价格待同步")
                .replace(ChatFormatting.RESET.toString(), "");
    }

    private String fit(String text, int maximumWidth) {
        if (font.width(text) <= maximumWidth) {
            return text;
        }
        return font.plainSubstrByWidth(text, Math.max(0, maximumWidth - font.width("...")))
                + "...";
    }
}

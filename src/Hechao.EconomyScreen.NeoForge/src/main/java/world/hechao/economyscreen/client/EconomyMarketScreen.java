package world.hechao.economyscreen.client;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.ChatFormatting;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ClickType;
import net.minecraft.world.item.ItemStack;

final class EconomyMarketScreen extends SinglePassBackgroundScreen {
    private static final int PAGE_BUTTON_WIDTH = 30;
    private static final int HOME_BUTTON_WIDTH = 80;
    private static final int SEARCH_DELAY_TICKS = 8;

    private final ChestMenu menu;
    private final String serverTitle;
    private EconomyMarketLayout.Layout layout;
    private Button previousButton;
    private Button nextButton;
    private EditBox searchBox;
    private ItemStack hovered = ItemStack.EMPTY;
    private String searchQuery = "";
    private String lastSubmittedSearch = "";
    private String observedPageLabel = "";
    private int localPage;
    private int searchDelay = -1;
    private boolean moveToLastPageAfterServerChange;

    EconomyMarketScreen(ChestMenu menu, String serverTitle) {
        super(Component.literal(serverTitle));
        this.menu = menu;
        this.serverTitle = serverTitle;
    }

    @Override
    protected void init() {
        layout = EconomyMarketLayout.calculate(width, height);
        addModeButtons();
        addFooterButtons();
        addSearchBox();
        observedPageLabel = pageInfoLabel();
        syncNavigation();
    }

    @Override
    public void tick() {
        super.tick();
        String pageLabel = pageInfoLabel();
        if (!pageLabel.equals(observedPageLabel)) {
            observedPageLabel = pageLabel;
            localPage = moveToLastPageAfterServerChange
                    ? maximumLocalPage()
                    : 0;
            moveToLastPageAfterServerChange = false;
            syncNavigation();
        }
        if (searchDelay > 0) {
            searchDelay--;
        }
        if (searchDelay == 0) {
            searchDelay = -1;
            submitSearch();
        }
    }

    @Override
    protected void renderContent(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        syncNavigation();
        IndustrialUiTheme.renderPanel(
                graphics,
                layout.panelLeft(),
                layout.panelTop(),
                layout.panelWidth(),
                layout.panelHeight());
        renderHeader(graphics);

        var offers = visibleOffers();
        hovered = ItemStack.EMPTY;
        if (offers.isEmpty()) {
            renderEmptyState(graphics);
        } else {
            int first = localPage * layout.pageSize();
            int last = Math.min(offers.size(), first + layout.pageSize());
            for (int index = first; index < last; index++) {
                renderOffer(graphics, offers.get(index), index - first, mouseX, mouseY);
            }
        }

        IndustrialUiTheme.renderDivider(
                graphics,
                layout.panelLeft() + 4,
                layout.panelLeft() + layout.panelWidth() - 4,
                layout.footerTop() - 1);
        renderPageIndicator(graphics);
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
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (button == 0) {
            var offer = offerAt(mouseX, mouseY);
            if (offer != null) {
                clickServerSlot(offer.slot());
                return true;
            }
        }
        return super.mouseClicked(mouseX, mouseY, button);
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

    private void addModeButtons() {
        addModeButton(0, tabLabel("玩家市场", "市场"), 47,
                ClientEconomyUiBridge.MARKET_TITLE);
        addModeButton(1, tabLabel("上架物品", "上架"), 45,
                ClientEconomyUiBridge.MARKET_LISTING_TITLE);
        addModeButton(2, tabLabel("我的挂单", "挂单"), 46,
                ClientEconomyUiBridge.MARKET_MINE_TITLE);
        addModeButton(3, tabLabel("待领取", "领取"), 52,
                ClientEconomyUiBridge.MARKET_DELIVERY_TITLE);
    }

    private void addModeButton(int index, String label, int slot, String targetTitle) {
        int column = index % layout.tabColumns();
        int row = index / layout.tabColumns();
        int x = layout.contentLeft()
                + column * (layout.tabWidth() + layout.tabGap());
        int y = layout.tabsTop()
                + row * (EconomyMarketLayout.BUTTON_HEIGHT + 4);
        var button = new IndustrialButton(
                x,
                y,
                layout.tabWidth(),
                EconomyMarketLayout.BUTTON_HEIGHT,
                Component.literal(label),
                ignored -> clickServerSlot(slot));
        boolean current = targetTitle.equals(serverTitle);
        button.active = !current;
        button.setTooltip(Tooltip.create(Component.literal(
                current ? "当前页面" : "切换到" + label)));
        addRenderableWidget(button);
    }

    private void addFooterButtons() {
        int buttonY = layout.footerTop() + 7;
        int homeWidth = Math.min(
                HOME_BUTTON_WIDTH,
                Math.max(40, layout.panelWidth() - 96));
        previousButton = new IndustrialButton(
                layout.panelLeft() + 12,
                buttonY,
                PAGE_BUTTON_WIDTH,
                EconomyMarketLayout.BUTTON_HEIGHT,
                Component.literal("<"),
                ignored -> changePage(-1));
        previousButton.setTooltip(Tooltip.create(Component.literal("上一页")));
        addRenderableWidget(previousButton);

        nextButton = new IndustrialButton(
                layout.panelLeft() + layout.panelWidth() - PAGE_BUTTON_WIDTH - 12,
                buttonY,
                PAGE_BUTTON_WIDTH,
                EconomyMarketLayout.BUTTON_HEIGHT,
                Component.literal(">"),
                ignored -> changePage(1));
        nextButton.setTooltip(Tooltip.create(Component.literal("下一页")));
        addRenderableWidget(nextButton);

        addRenderableWidget(new IndustrialButton(
                width / 2 - homeWidth / 2,
                buttonY,
                homeWidth,
                EconomyMarketLayout.BUTTON_HEIGHT,
                Component.literal(homeWidth < 70 ? "首页" : "返回首页"),
                ignored -> returnHome()));
    }

    private void addSearchBox() {
        boolean compact = layout.panelWidth() < 260;
        int searchWidth = compact
                ? Math.max(82, layout.panelWidth() - 70)
                : Math.min(148, Math.max(92, layout.panelWidth() - 230));
        int searchX = compact
                ? layout.panelLeft() + (layout.panelWidth() - searchWidth) / 2
                : layout.panelLeft() + layout.panelWidth() - searchWidth - 58;
        searchBox = new EditBox(
                font,
                searchX + 5,
                layout.panelTop() + 10,
                searchWidth - 10,
                16,
                Component.literal(searchHint()));
        searchBox.setBordered(false);
        searchBox.setMaxLength(48);
        searchBox.setHint(Component.literal(searchHint()));
        searchBox.setValue(searchQuery);
        searchBox.setResponder(value -> {
            searchQuery = value;
            localPage = 0;
            searchDelay = SEARCH_DELAY_TICKS;
        });
        addRenderableWidget(searchBox);
    }

    private void submitSearch() {
        String normalized = searchQuery.trim();
        if (normalized.equals(lastSubmittedSearch)) {
            return;
        }
        var connection = minecraft == null ? null : minecraft.getConnection();
        if (connection == null) {
            return;
        }
        lastSubmittedSearch = normalized;
        connection.sendCommand(EconomyMarketSearch.encode(normalized).marketCommand());
    }

    private void renderHeader(GuiGraphics graphics) {
        boolean compact = layout.panelWidth() < 260;
        if (!compact) {
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
        }
        if (searchBox != null) {
            IndustrialUiTheme.renderInputField(
                    graphics,
                    searchBox.getX() - 5,
                    layout.panelTop() + 9,
                    searchBox.getWidth() + 10,
                    18,
                    searchBox.isFocused());
        }
        if (!compact) {
            String count = serverPageInfo().totalItemCount() + " 项";
            graphics.drawString(
                    font,
                    count,
                    layout.panelLeft() + layout.panelWidth() - 12 - font.width(count),
                    layout.panelTop() + 11,
                    0xFFB7BBC0,
                    false);
        }
    }

    private void renderEmptyState(GuiGraphics graphics) {
        int centerY = layout.contentTop()
                + (layout.footerTop() - layout.contentTop()) / 2;
        String heading;
        String detail;
        if (!searchQuery.isBlank()) {
            heading = "没有匹配的商品";
            detail = ClientEconomyUiBridge.MARKET_DELIVERY_TITLE.equals(serverTitle)
                    ? "可搜索中文名、物品 ID 或命名空间"
                    : "可搜索中文名、物品 ID 或卖家";
        } else if (ClientEconomyUiBridge.MARKET_DELIVERY_TITLE.equals(serverTitle)) {
            heading = "暂无待领取物品";
            detail = "购买、下架和到期物品会进入这里";
        } else if (ClientEconomyUiBridge.MARKET_MINE_TITLE.equals(serverTitle)) {
            heading = "暂无活动挂单";
            detail = "通过上架物品创建第一条挂单";
        } else {
            heading = "玩家市场暂无商品";
            detail = "其他玩家上架后会实时显示";
        }
        graphics.drawCenteredString(font, heading, width / 2, centerY - 8, 0xFFFFFFFF);
        graphics.drawCenteredString(font, detail, width / 2, centerY + 8, 0xFF9FA4A9);
    }

    private void renderOffer(
            GuiGraphics graphics,
            SlotOffer offer,
            int relative,
            int mouseX,
            int mouseY) {
        int column = relative % layout.columns();
        int row = relative / layout.columns();
        int x = layout.contentLeft()
                + column * (layout.cardWidth() + EconomyMarketLayout.CARD_GAP);
        int y = layout.contentTop()
                + row * (layout.cardHeight() + EconomyMarketLayout.CARD_GAP);
        boolean isHovered = inside(mouseX, mouseY, x, y,
                layout.cardWidth(), layout.cardHeight());
        IndustrialUiTheme.renderCard(
                graphics,
                x,
                y,
                layout.cardWidth(),
                layout.cardHeight(),
                isHovered);
        graphics.renderItem(
                offer.stack(),
                x + 8,
                y + Math.max(4, (layout.cardHeight() - 16) / 2));
        graphics.drawString(
                font,
                fit(offer.stack().getHoverName().getString(), layout.cardWidth() - 38),
                x + 30,
                y + (layout.cardHeight() < 36 ? 10 : 7),
                0xFFFFFFFF,
                false);
        if (layout.cardHeight() >= 36) {
            graphics.drawString(
                    font,
                    fit(summary(offer.stack()), layout.cardWidth() - 38),
                    x + 30,
                    y + 20,
                    ClientEconomyUiBridge.MARKET_DELIVERY_TITLE.equals(serverTitle)
                            ? 0xFF8CD99B
                            : 0xFFFFD75A,
                    false);
        }
        if (isHovered) {
            hovered = offer.stack();
        }
    }

    private void renderPageIndicator(GuiGraphics graphics) {
        var pageInfo = serverPageInfo();
        int localPageCount = maximumLocalPage() + 1;
        if (pageInfo.pageCount() <= 1 && localPageCount <= 1) {
            return;
        }
        String indicator = pageInfo.pageCount() > 1
                ? "第 " + pageInfo.page() + " / " + pageInfo.pageCount()
                        + " 页 · 视图 " + (localPage + 1) + " / " + localPageCount
                : (localPage + 1) + " / " + localPageCount;
        graphics.drawCenteredString(
                font,
                indicator,
                width / 2,
                layout.footerTop() - 10,
                0xFFB7BBC0);
    }

    private SlotOffer offerAt(double mouseX, double mouseY) {
        var offers = visibleOffers();
        int first = localPage * layout.pageSize();
        int last = Math.min(offers.size(), first + layout.pageSize());
        for (int index = first; index < last; index++) {
            int relative = index - first;
            int column = relative % layout.columns();
            int row = relative / layout.columns();
            int x = layout.contentLeft()
                    + column * (layout.cardWidth() + EconomyMarketLayout.CARD_GAP);
            int y = layout.contentTop()
                    + row * (layout.cardHeight() + EconomyMarketLayout.CARD_GAP);
            if (inside(mouseX, mouseY, x, y,
                    layout.cardWidth(), layout.cardHeight())) {
                return offers.get(index);
            }
        }
        return null;
    }

    private List<SlotOffer> visibleOffers() {
        var offers = new ArrayList<SlotOffer>();
        int maximum = Math.min(EconomyMarketServerPage.ITEM_SLOTS, menu.getRowCount() * 9);
        for (int slot = 0; slot < maximum; slot++) {
            var stack = menu.getSlot(slot).getItem();
            if (!stack.isEmpty()) {
                offers.add(new SlotOffer(slot, stack));
            }
        }
        return List.copyOf(offers);
    }

    private String summary(ItemStack stack) {
        var lore = stack.get(DataComponents.LORE);
        if (lore == null || lore.lines().isEmpty()) {
            return ClientEconomyUiBridge.MARKET_DELIVERY_TITLE.equals(serverTitle)
                    ? "点击领取"
                    : "价格待同步";
        }
        return lore.lines().getFirst().getString()
                .replace(ChatFormatting.RESET.toString(), "");
    }

    private void changePage(int direction) {
        if (direction < 0) {
            if (localPage > 0) {
                localPage--;
            } else if (hasServerControl(
                    EconomyMarketServerPage.PREVIOUS_SLOT,
                    EconomyMarketServerPage.PREVIOUS_LABEL)) {
                moveToLastPageAfterServerChange = true;
                clickServerSlot(EconomyMarketServerPage.PREVIOUS_SLOT);
            }
        } else if (direction > 0) {
            if (localPage < maximumLocalPage()) {
                localPage++;
            } else if (hasServerControl(
                    EconomyMarketServerPage.NEXT_SLOT,
                    EconomyMarketServerPage.NEXT_LABEL)) {
                localPage = 0;
                clickServerSlot(EconomyMarketServerPage.NEXT_SLOT);
            }
        }
        syncNavigation();
    }

    private void syncNavigation() {
        if (previousButton == null || nextButton == null || layout == null) {
            return;
        }
        localPage = Math.min(localPage, maximumLocalPage());
        boolean canPrevious = canMovePrevious();
        boolean canNext = canMoveNext();
        previousButton.visible = canPrevious || canNext;
        nextButton.visible = canPrevious || canNext;
        previousButton.active = canPrevious;
        nextButton.active = canNext;
    }

    private int maximumLocalPage() {
        return EconomyMarketLayout.maximumPage(visibleOffers().size(), layout.pageSize());
    }

    private boolean canMovePrevious() {
        return localPage > 0 || hasServerControl(
                EconomyMarketServerPage.PREVIOUS_SLOT,
                EconomyMarketServerPage.PREVIOUS_LABEL);
    }

    private boolean canMoveNext() {
        return localPage < maximumLocalPage() || hasServerControl(
                EconomyMarketServerPage.NEXT_SLOT,
                EconomyMarketServerPage.NEXT_LABEL);
    }

    private boolean hasServerControl(int slot, String expectedLabel) {
        if (slot >= menu.slots.size()) {
            return false;
        }
        var item = menu.getSlot(slot).getItem();
        return !item.isEmpty() && expectedLabel.equals(item.getHoverName().getString());
    }

    private EconomyMarketServerPage.Info serverPageInfo() {
        return EconomyMarketServerPage.parse(pageInfoLabel(), visibleOffers().size());
    }

    private String pageInfoLabel() {
        if (EconomyMarketServerPage.PAGE_INFO_SLOT >= menu.slots.size()) {
            return "";
        }
        var item = menu.getSlot(EconomyMarketServerPage.PAGE_INFO_SLOT).getItem();
        return item.isEmpty() ? "" : item.getHoverName().getString();
    }

    private void clickServerSlot(int slot) {
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
        clickServerSlot(53);
    }

    private String searchHint() {
        return ClientEconomyUiBridge.MARKET_DELIVERY_TITLE.equals(serverTitle)
                ? "搜索待领取商品"
                : "搜索商品或卖家";
    }

    private String tabLabel(String full, String compact) {
        return layout.tabWidth() < 60 ? compact : full;
    }

    private String fit(String text, int maximumWidth) {
        if (font.width(text) <= maximumWidth) {
            return text;
        }
        return font.plainSubstrByWidth(text, Math.max(0, maximumWidth - font.width("...")))
                + "...";
    }

    private static boolean inside(
            double mouseX,
            double mouseY,
            int x,
            int y,
            int width,
            int height) {
        return mouseX >= x && mouseX < x + width && mouseY >= y && mouseY < y + height;
    }

    private record SlotOffer(int slot, ItemStack stack) {
    }
}

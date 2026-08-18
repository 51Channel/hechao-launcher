package world.hechao.economyscreen.client;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.ChatFormatting;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.item.ItemStack;

final class EconomyCatalogScreen extends Screen {
    private static final int PAGE_BUTTON_WIDTH = 30;
    private static final int CLOSE_BUTTON_WIDTH = 80;
    private static final int BUTTON_HEIGHT = 20;

    private final ChestMenu menu;
    private EconomyCatalogLayout.Layout layout;
    private Button previousButton;
    private Button nextButton;
    private int page;

    EconomyCatalogScreen(ChestMenu menu) {
        super(Component.literal(ClientEconomyUiBridge.CATALOG_TITLE));
        this.menu = menu;
    }

    @Override
    protected void init() {
        layout = EconomyCatalogLayout.calculate(width, height);
        int buttonY = layout.footerTop() + 7;
        previousButton = Button.builder(
                        Component.literal("<"),
                        ignored -> changePage(-1))
                .bounds(
                        layout.panelLeft() + 12,
                        buttonY,
                        PAGE_BUTTON_WIDTH,
                        BUTTON_HEIGHT)
                .tooltip(Tooltip.create(Component.literal("上一页")))
                .build();
        addRenderableWidget(previousButton);

        nextButton = Button.builder(
                        Component.literal(">"),
                        ignored -> changePage(1))
                .bounds(
                        layout.panelLeft() + layout.panelWidth()
                                - PAGE_BUTTON_WIDTH - 12,
                        buttonY,
                        PAGE_BUTTON_WIDTH,
                        BUTTON_HEIGHT)
                .tooltip(Tooltip.create(Component.literal("下一页")))
                .build();
        addRenderableWidget(nextButton);

        addRenderableWidget(Button.builder(
                        Component.literal("完成"),
                        ignored -> onClose())
                .bounds(
                        width / 2 - CLOSE_BUTTON_WIDTH / 2,
                        buttonY,
                        CLOSE_BUTTON_WIDTH,
                        BUTTON_HEIGHT)
                .build());
        syncNavigation(products().size());
    }

    @Override
    public void render(
            GuiGraphics graphics,
            int mouseX,
            int mouseY,
            float partialTick) {
        var products = products();
        syncNavigation(products.size());
        renderBackground(graphics, mouseX, mouseY, partialTick);
        graphics.fill(
                layout.panelLeft(),
                layout.panelTop(),
                layout.panelLeft() + layout.panelWidth(),
                layout.panelTop() + layout.panelHeight(),
                0xED1C1E21);
        graphics.renderOutline(
                layout.panelLeft(),
                layout.panelTop(),
                layout.panelWidth(),
                layout.panelHeight(),
                0xFF777B80);
        graphics.fill(
                layout.panelLeft() + 1,
                layout.panelTop() + 31,
                layout.panelLeft() + layout.panelWidth() - 1,
                layout.panelTop() + 32,
                0xFFD6A947);
        graphics.drawString(
                font,
                title,
                layout.panelLeft() + 12,
                layout.panelTop() + 11,
                0xFFFFFFFF,
                true);
        graphics.drawString(
                font,
                products.size() + " 项",
                layout.panelLeft() + layout.panelWidth()
                        - 12 - font.width(products.size() + " 项"),
                layout.panelTop() + 11,
                0xFFB7BBC0,
                false);

        ItemStack hovered = ItemStack.EMPTY;
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
                graphics.fill(
                        x,
                        y,
                        x + layout.cardWidth(),
                        y + EconomyCatalogLayout.CARD_HEIGHT,
                        isHovered ? 0xF0474B50 : 0xD5323539);
                graphics.renderOutline(
                        x,
                        y,
                        layout.cardWidth(),
                        EconomyCatalogLayout.CARD_HEIGHT,
                        isHovered ? 0xFFD6A947 : 0xFF55595E);
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

        if (maximumPage(products.size()) > 0) {
            String indicator = (page + 1) + " / " + (maximumPage(products.size()) + 1);
            graphics.drawCenteredString(
                    font,
                    indicator,
                    width / 2,
                    layout.footerTop() - 10,
                    0xFFB7BBC0);
        }
        super.render(graphics, mouseX, mouseY, partialTick);
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
        if (scrollY == 0 || maximumPage(products().size()) == 0) {
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
        int productSlots = menu.getRowCount() * 9;
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
        page = Math.max(0, Math.min(maximum, page + direction));
        syncNavigation(products().size());
    }

    private void syncNavigation(int itemCount) {
        if (previousButton == null || nextButton == null) {
            return;
        }
        int maximum = maximumPage(itemCount);
        page = Math.min(page, maximum);
        previousButton.visible = maximum > 0;
        nextButton.visible = maximum > 0;
        previousButton.active = page > 0;
        nextButton.active = page < maximum;
    }

    private int maximumPage(int itemCount) {
        return EconomyCatalogLayout.maximumPage(itemCount, layout.pageSize());
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

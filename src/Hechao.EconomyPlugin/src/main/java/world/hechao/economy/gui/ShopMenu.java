package world.hechao.economy.gui;

import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import org.bukkit.Bukkit;
import org.bukkit.ChatColor;
import org.bukkit.Material;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.inventory.InventoryClickEvent;
import org.bukkit.event.inventory.InventoryCloseEvent;
import org.bukkit.inventory.Inventory;
import org.bukkit.inventory.ItemStack;
import world.hechao.economy.api.EconomyGateway.Product;

public final class ShopMenu implements Listener {
    public static final String SHOP_TITLE = "赫朝官方商城";
    public static final String BUYBACK_TITLE = "赫朝回收目录";
    private static final String PREVIOUS_LABEL = "上一批";
    private static final String NEXT_LABEL = "下一批";
    private final Map<UUID, MenuSession> openMenus = new ConcurrentHashMap<>();
    private final ShopPurchaseMenu purchaseMenu;

    public ShopMenu(ShopPurchaseMenu purchaseMenu) {
        this.purchaseMenu = purchaseMenu;
    }

    public void openShop(Player player, List<Product> products) {
        open(player, products, Mode.SHOP);
    }

    public void openBuyback(Player player, List<Product> products) {
        open(player, products, Mode.BUYBACK);
    }

    public boolean search(Player player, MarketSearchRequest request) {
        var session = openMenus.get(player.getUniqueId());
        if (session == null) {
            return false;
        }
        session.query = request.query();
        session.translatedItemIds = request.translatedItemIds();
        renderPage(session, 0);
        return true;
    }

    private void open(Player player, List<Product> products, Mode mode) {
        var inventory = Bukkit.createInventory(
                null,
                ShopMenuPagination.INVENTORY_SIZE,
                mode.title);
        var session = new MenuSession(inventory, List.copyOf(products), mode);
        renderPage(session, 0);
        openMenus.put(player.getUniqueId(), session);
        player.openInventory(inventory);
    }

    private void renderPage(MenuSession session, int requestedPage) {
        var products = filteredProducts(session);
        int page = ShopMenuPagination.clampPage(requestedPage, products.size());
        session.page = page;
        session.inventory.clear();
        int first = ShopMenuPagination.firstProductIndex(page, products.size());
        int count = ShopMenuPagination.productsOnPage(page, products.size());
        if (products.isEmpty()) {
            session.inventory.setItem(
                    22,
                    emptyStateItem(session.mode, session.query.isBlank()));
        }
        for (int slot = 0; slot < count; slot++) {
            var product = products.get(first + slot);
            var material = Material.matchMaterial(product.itemId());
            if (material == null || material.isAir()) {
                continue;
            }
            var item = new ItemStack(material);
            var meta = item.getItemMeta();
            if (session.mode == Mode.SHOP) {
                meta.setLore(List.of(
                        ChatColor.GOLD + "购买价: "
                                + product.shopUnitPrice().toPlainString() + " 金币",
                        ChatColor.GRAY + "回收价: " + product.unitPrice().toPlainString()
                                + " 金币",
                        ChatColor.GRAY + "一次最多购买: 2304",
                        ChatColor.GREEN + "点击进入购买确认"));
            } else {
                meta.setLore(List.of(
                        ChatColor.GOLD + "回收价: " + product.unitPrice().toPlainString()
                                + " 金币",
                        ChatColor.GRAY + "个人日限: " + product.personalDailyLimit(),
                        ChatColor.GRAY + "全服日限: " + product.serverDailyLimit(),
                        ChatColor.DARK_GRAY + "使用 /sell 放入物品回收"));
            }
            item.setItemMeta(meta);
            session.inventory.setItem(slot, item);
        }

        int pageCount = ShopMenuPagination.pageCount(products.size());
        if (page > 0) {
            session.inventory.setItem(
                    ShopMenuPagination.PREVIOUS_SLOT,
                    navigationItem(PREVIOUS_LABEL));
        }
        session.inventory.setItem(
                ShopMenuPagination.PAGE_INFO_SLOT,
                pageInfoItem(page, pageCount, products.size(), session.query));
        if (page + 1 < pageCount) {
            session.inventory.setItem(
                    ShopMenuPagination.NEXT_SLOT,
                    navigationItem(NEXT_LABEL));
        }
    }

    private ItemStack emptyStateItem(Mode mode, boolean noSearch) {
        var item = new ItemStack(Material.BARRIER);
        var meta = item.getItemMeta();
        boolean shop = mode == Mode.SHOP;
        String label = noSearch
                ? shop ? "商城暂未上架商品" : "回收目录暂无商品"
                : "没有匹配的商品";
        meta.setDisplayName(ChatColor.RED + label);
        meta.setLore(List.of(
                ChatColor.GRAY + (shop
                        ? "商城售价由服主单独配置"
                        : "回收价由服主单独配置"),
                ChatColor.DARK_GRAY + (shop
                        ? "回收价和购买价不是同一个价格"
                        : "请稍后重试或联系服主")));
        item.setItemMeta(meta);
        return item;
    }

    private ItemStack navigationItem(String label) {
        var item = new ItemStack(Material.ARROW);
        var meta = item.getItemMeta();
        meta.setDisplayName(ChatColor.WHITE + label);
        item.setItemMeta(meta);
        return item;
    }

    private ItemStack pageInfoItem(
            int page,
            int pageCount,
            int productCount,
            String query) {
        var item = new ItemStack(Material.PAPER);
        var meta = item.getItemMeta();
        meta.setDisplayName(ChatColor.GOLD + "第 " + (page + 1) + " / " + pageCount
                + " 批 · 共 " + productCount + " 项");
        meta.setLore(List.of(ChatColor.GRAY + (query.isBlank()
                ? "目录由服务器实时提供"
                : "当前搜索: " + query)));
        item.setItemMeta(meta);
        return item;
    }

    @EventHandler
    public void onClick(InventoryClickEvent event) {
        if (!(event.getWhoClicked() instanceof Player player)) {
            return;
        }
        var session = openMenus.get(player.getUniqueId());
        if (session == null || event.getView().getTopInventory() != session.inventory) {
            return;
        }

        event.setCancelled(true);
        int rawSlot = event.getRawSlot();
        if (rawSlot == ShopMenuPagination.PREVIOUS_SLOT && session.page > 0) {
            renderPage(session, session.page - 1);
        } else if (rawSlot == ShopMenuPagination.NEXT_SLOT
                && session.page + 1
                        < ShopMenuPagination.pageCount(filteredProducts(session).size())) {
            renderPage(session, session.page + 1);
        } else if (session.mode == Mode.SHOP
                && rawSlot >= 0
                && rawSlot < ShopMenuPagination.PRODUCT_SLOTS) {
            var products = filteredProducts(session);
            int index = ShopMenuPagination.firstProductIndex(session.page, products.size())
                    + rawSlot;
            if (index < products.size()) {
                var product = products.get(index);
                openMenus.remove(player.getUniqueId(), session);
                purchaseMenu.open(player, product);
            }
        }
    }

    @EventHandler
    public void onClose(InventoryCloseEvent event) {
        var session = openMenus.get(event.getPlayer().getUniqueId());
        if (session != null && event.getView().getTopInventory() == session.inventory) {
            openMenus.remove(event.getPlayer().getUniqueId(), session);
        }
    }

    private static List<Product> filteredProducts(MenuSession session) {
        return session.products.stream()
                .filter(product -> session.mode == Mode.BUYBACK
                        || product.shopUnitPrice() != null)
                .filter(product -> MarketplaceSearch.matches(
                        session.query,
                        session.translatedItemIds,
                        product.itemId(),
                        ""))
                .toList();
    }

    private enum Mode {
        SHOP(SHOP_TITLE),
        BUYBACK(BUYBACK_TITLE);

        private final String title;

        Mode(String title) {
            this.title = title;
        }
    }

    private static final class MenuSession {
        private final Inventory inventory;
        private final List<Product> products;
        private final Mode mode;
        private String query = "";
        private Set<String> translatedItemIds = Set.of();
        private int page;

        private MenuSession(Inventory inventory, List<Product> products, Mode mode) {
            this.inventory = inventory;
            this.products = products;
            this.mode = mode;
        }
    }
}

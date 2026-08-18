package world.hechao.economy.gui;

import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import org.bukkit.Bukkit;
import org.bukkit.ChatColor;
import org.bukkit.Material;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.inventory.InventoryCloseEvent;
import org.bukkit.event.inventory.InventoryClickEvent;
import org.bukkit.inventory.Inventory;
import org.bukkit.inventory.ItemStack;
import world.hechao.economy.api.EconomyGateway.Product;

public final class ShopMenu implements Listener {
    private static final String TITLE = "赫朝回收目录";
    private static final String PREVIOUS_LABEL = "上一批";
    private static final String NEXT_LABEL = "下一批";
    private final Map<java.util.UUID, MenuSession> openMenus = new ConcurrentHashMap<>();

    public void open(Player player, List<Product> products) {
        var inventory = Bukkit.createInventory(
                null,
                ShopMenuPagination.INVENTORY_SIZE,
                TITLE);
        var session = new MenuSession(inventory, List.copyOf(products));
        renderPage(session, 0);
        player.openInventory(inventory);
        openMenus.put(player.getUniqueId(), session);
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

    private void renderPage(MenuSession session, int requestedPage) {
        var products = filteredProducts(session);
        int page = ShopMenuPagination.clampPage(requestedPage, products.size());
        session.page = page;
        session.inventory.clear();
        int first = ShopMenuPagination.firstProductIndex(page, products.size());
        int count = ShopMenuPagination.productsOnPage(page, products.size());
        for (int slot = 0; slot < count; slot++) {
            var product = products.get(first + slot);
            var material = Material.matchMaterial(product.itemId());
            if (material == null || material.isAir()) {
                continue;
            }
            var item = new ItemStack(material);
            var meta = item.getItemMeta();
            meta.setLore(List.of(
                    ChatColor.GOLD + "回收价: " + product.unitPrice().toPlainString() + " 金币",
                    ChatColor.GRAY + "个人日限: " + product.personalDailyLimit(),
                    ChatColor.GRAY + "全服日限: " + product.serverDailyLimit(),
                    ChatColor.DARK_GRAY + "使用 /sell 放入物品回收"));
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
        if (event.getRawSlot() == ShopMenuPagination.PREVIOUS_SLOT && session.page > 0) {
            renderPage(session, session.page - 1);
        } else if (event.getRawSlot() == ShopMenuPagination.NEXT_SLOT
                && session.page + 1
                        < ShopMenuPagination.pageCount(filteredProducts(session).size())) {
            renderPage(session, session.page + 1);
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
                .filter(product -> MarketplaceSearch.matches(
                        session.query,
                        session.translatedItemIds,
                        product.itemId(),
                        ""))
                .toList();
    }

    private static final class MenuSession {
        private final Inventory inventory;
        private final List<Product> products;
        private String query = "";
        private Set<String> translatedItemIds = Set.of();
        private int page;

        private MenuSession(Inventory inventory, List<Product> products) {
            this.inventory = inventory;
            this.products = products;
        }
    }
}

package world.hechao.economy.gui;

import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.bukkit.Bukkit;
import org.bukkit.ChatColor;
import org.bukkit.Material;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.inventory.InventoryClickEvent;
import org.bukkit.inventory.Inventory;
import org.bukkit.inventory.ItemStack;
import world.hechao.economy.api.EconomyGateway.Product;

public final class ShopMenu implements Listener {
    private static final String TITLE = "赫朝回收目录";
    private final Map<java.util.UUID, Inventory> openMenus = new ConcurrentHashMap<>();

    public void open(Player player, List<Product> products) {
        int size = Math.max(9, Math.min(54, ((products.size() + 8) / 9) * 9));
        var inventory = Bukkit.createInventory(null, size, TITLE);
        for (int index = 0; index < Math.min(products.size(), size); index++) {
            var product = products.get(index);
            var material = Material.matchMaterial(product.itemId());
            if (material == null || material.isAir()) {
                continue;
            }
            var item = new ItemStack(material);
            var meta = item.getItemMeta();
            meta.setDisplayName(ChatColor.WHITE + material.translationKey());
            meta.setLore(List.of(
                    ChatColor.GOLD + "回收价: " + product.unitPrice().toPlainString() + " 金币",
                    ChatColor.GRAY + "个人日限: " + product.personalDailyLimit(),
                    ChatColor.GRAY + "全服日限: " + product.serverDailyLimit(),
                    ChatColor.DARK_GRAY + "手持物品使用 /sell"));
            item.setItemMeta(meta);
            inventory.setItem(index, item);
        }
        openMenus.put(player.getUniqueId(), inventory);
        player.openInventory(inventory);
    }

    @EventHandler
    public void onClick(InventoryClickEvent event) {
        if (!(event.getWhoClicked() instanceof Player player)) {
            return;
        }
        var expected = openMenus.get(player.getUniqueId());
        if (expected != null && event.getView().getTopInventory() == expected) {
            event.setCancelled(true);
        }
    }
}

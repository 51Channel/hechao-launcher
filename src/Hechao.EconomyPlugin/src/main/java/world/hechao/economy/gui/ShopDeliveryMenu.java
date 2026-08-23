package world.hechao.economy.gui;

import java.math.BigDecimal;
import java.math.RoundingMode;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ConcurrentHashMap;
import java.util.function.Consumer;
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
import world.hechao.economy.HechaoEconomyPlugin;
import world.hechao.economy.api.EconomyGateway;
import world.hechao.economy.api.EconomyGatewayException;
import world.hechao.economy.inventory.QuarantinedSaleStore;

public final class ShopDeliveryMenu implements Listener {
    public static final String TITLE = "赫朝商城待领取";
    private static final int INVENTORY_SIZE = 54;
    private static final int PAGE_SIZE = 45;
    private static final int PAGE_INFO_SLOT = 49;
    private static final int RETURN_SLOT = 53;
    private static final String PREFIX = ChatColor.GOLD + "[赫朝经济] " + ChatColor.WHITE;

    private final HechaoEconomyPlugin plugin;
    private final QuarantinedSaleStore quarantinedSales;
    private final Map<UUID, Session> sessions = new ConcurrentHashMap<>();
    private final java.util.Set<UUID> claiming = ConcurrentHashMap.newKeySet();

    public ShopDeliveryMenu(
            HechaoEconomyPlugin plugin,
            QuarantinedSaleStore quarantinedSales) {
        this.plugin = plugin;
        this.quarantinedSales = quarantinedSales;
    }

    public void open(Player player, List<EconomyGateway.ShopDelivery> deliveries) {
        var inventory = Bukkit.createInventory(null, INVENTORY_SIZE, TITLE);
        var session = new Session(inventory, deliveries);
        sessions.put(player.getUniqueId(), session);
        render(session);
        player.openInventory(inventory);
    }

    public void closeAll() {
        for (var entry : List.copyOf(sessions.entrySet())) {
            var player = Bukkit.getPlayer(entry.getKey());
            if (player != null) {
                player.closeInventory();
            }
            sessions.remove(entry.getKey(), entry.getValue());
        }
    }

    @EventHandler
    public void onClick(InventoryClickEvent event) {
        if (!(event.getWhoClicked() instanceof Player player)) {
            return;
        }
        var session = sessions.get(player.getUniqueId());
        if (session == null || event.getView().getTopInventory() != session.inventory) {
            return;
        }
        event.setCancelled(true);
        int slot = event.getRawSlot();
        if (slot == RETURN_SLOT) {
            sessions.remove(player.getUniqueId(), session);
            player.closeInventory();
            Bukkit.getScheduler().runTask(plugin,
                    () -> player.performCommand("shop"));
            return;
        }
        if (slot < 0 || slot >= PAGE_SIZE || slot >= session.deliveries.size()) {
            return;
        }
        claim(player, session, session.deliveries.get(slot));
    }

    @EventHandler
    public void onClose(InventoryCloseEvent event) {
        var session = sessions.get(event.getPlayer().getUniqueId());
        if (session != null && event.getView().getTopInventory() == session.inventory) {
            sessions.remove(event.getPlayer().getUniqueId(), session);
        }
    }

    private void render(Session session) {
        session.inventory.clear();
        for (int slot = 0; slot < Math.min(PAGE_SIZE, session.deliveries.size()); slot++) {
            var delivery = session.deliveries.get(slot);
            var material = Material.matchMaterial(delivery.itemId());
            if (material == null || material.isAir()) {
                continue;
            }
            var stack = new ItemStack(
                    material,
                    Math.min(delivery.quantity(), material.getMaxStackSize()));
            var meta = stack.getItemMeta();
            meta.setLore(List.of(
                    ChatColor.GRAY + "数量: " + delivery.quantity(),
                    ChatColor.GRAY + "购买价: " + money(delivery.unitPrice()) + " / 个",
                    ChatColor.DARK_GRAY + "点击领取到背包"));
            stack.setItemMeta(meta);
            session.inventory.setItem(slot, stack);
        }
        session.inventory.setItem(
                PAGE_INFO_SLOT,
                control(
                        Material.PAPER,
                        "待领取 " + session.deliveries.size() + " 项",
                        "购买物品不会直接掉落到世界"));
        session.inventory.setItem(
                RETURN_SLOT,
                control(Material.BARRIER, "返回商城", "返回官方商城"));
    }

    private void claim(
            Player player,
            Session session,
            EconomyGateway.ShopDelivery delivery) {
        if (!claiming.add(delivery.deliveryId())) {
            return;
        }
        var material = Material.matchMaterial(delivery.itemId());
        if (material == null || material.isAir()) {
            claiming.remove(delivery.deliveryId());
            player.sendMessage(PREFIX + ChatColor.RED
                    + "当前客户端无法识别该物品，不能领取。");
            return;
        }
        if (!canFit(player, material, delivery.quantity())) {
            claiming.remove(delivery.deliveryId());
            player.sendMessage(PREFIX + ChatColor.RED
                    + "背包空间不足，请整理后再领取。");
            return;
        }

        UUID operationId = UUID.randomUUID();
        async(
                () -> plugin.gateway().shopClaim(
                        "shop-claim:" + operationId,
                        delivery.deliveryId(),
                        player.getUniqueId()),
                response -> {
                    claiming.remove(delivery.deliveryId());
                    if (!"Applied".equals(response.status())) {
                        player.sendMessage(PREFIX + ChatColor.RED
                                + failureMessage(response.failureCode()) + "。");
                        return;
                    }
                    int leftover = give(player, material, response.quantity());
                    if (leftover > 0) {
                        quarantinedSales.add(
                                player.getUniqueId(),
                                response.operationId(),
                                response.itemId(),
                                leftover,
                                "SHOP_DELIVERY_INVENTORY_RACE");
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "领取时背包发生变化，剩余物品已进入隔离记录。");
                    }
                    session.deliveries = session.deliveries.stream()
                            .filter(item -> !item.deliveryId().equals(delivery.deliveryId()))
                            .toList();
                    render(session);
                    player.sendMessage(PREFIX + "已领取 " + response.itemId()
                            + " × " + (response.quantity() - leftover) + "。");
                },
                exception -> {
                    claiming.remove(delivery.deliveryId());
                    if (exception.isOutcomeUnknown()) {
                        quarantinedSales.add(
                                player.getUniqueId(),
                                delivery.deliveryId(),
                                delivery.itemId(),
                                delivery.quantity(),
                                "SHOP_CLAIM_OUTCOME_UNKNOWN");
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "领取结果暂时无法确认，已写入隔离记录。");
                    } else {
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "经济服务暂时无法完成领取。");
                    }
                });
    }

    private static boolean canFit(Player player, Material material, int quantity) {
        int remaining = quantity;
        for (ItemStack stack : player.getInventory().getStorageContents()) {
            if (stack == null || stack.getType().isAir()) {
                remaining -= material.getMaxStackSize();
            } else if (stack.getType() == material && stack.getAmount() < stack.getMaxStackSize()) {
                remaining -= stack.getMaxStackSize() - stack.getAmount();
            }
            if (remaining <= 0) {
                return true;
            }
        }
        return false;
    }

    private static int give(Player player, Material material, int quantity) {
        int remaining = quantity;
        while (remaining > 0) {
            int amount = Math.min(remaining, material.getMaxStackSize());
            var leftovers = player.getInventory().addItem(new ItemStack(material, amount));
            int leftover = leftovers.values().stream()
                    .mapToInt(ItemStack::getAmount)
                    .sum();
            remaining -= amount - leftover;
            if (leftover > 0) {
                break;
            }
        }
        return remaining;
    }

    private static ItemStack control(Material material, String name, String lore) {
        var stack = new ItemStack(material);
        var meta = stack.getItemMeta();
        meta.setDisplayName(ChatColor.WHITE + name);
        meta.setLore(List.of(ChatColor.GRAY + lore));
        stack.setItemMeta(meta);
        return stack;
    }

    private static String money(BigDecimal amount) {
        return amount.setScale(2, RoundingMode.HALF_UP).toPlainString() + " 金币";
    }

    private static String failureMessage(String code) {
        return "DELIVERY_ALREADY_CLAIMED".equals(code)
                ? "该物品已经领取"
                : "经济服务拒绝了本次领取";
    }

    private <T> void async(
            CheckedSupplier<T> operation,
            Consumer<T> success,
            Consumer<EconomyGatewayException> failure) {
        java.util.concurrent.CompletableFuture
                .supplyAsync(() -> {
                    try {
                        return operation.get();
                    } catch (EconomyGatewayException exception) {
                        throw new CompletionException(exception);
                    }
                }, plugin.executor())
                .whenComplete((value, throwable) -> Bukkit.getScheduler().runTask(plugin, () -> {
                    if (throwable == null) {
                        success.accept(value);
                        return;
                    }
                    Throwable cause = throwable instanceof CompletionException
                            ? throwable.getCause()
                            : throwable;
                    failure.accept(cause instanceof EconomyGatewayException gatewayException
                            ? gatewayException
                            : new EconomyGatewayException(
                                    "unexpected economy failure",
                                    cause,
                                    true));
                }));
    }

    @FunctionalInterface
    private interface CheckedSupplier<T> {
        T get() throws EconomyGatewayException;
    }

    private static final class Session {
        private final Inventory inventory;
        private List<EconomyGateway.ShopDelivery> deliveries;

        private Session(
                Inventory inventory,
                List<EconomyGateway.ShopDelivery> deliveries) {
            this.inventory = inventory;
            this.deliveries = List.copyOf(deliveries);
        }
    }
}

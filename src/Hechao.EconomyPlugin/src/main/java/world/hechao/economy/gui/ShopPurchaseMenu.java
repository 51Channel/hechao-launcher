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

public final class ShopPurchaseMenu implements Listener {
    public static final String TITLE = "赫朝商城确认购买";
    public static final int ITEM_SLOT = 13;
    public static final int MINUS_LARGE_SLOT = 10;
    public static final int MINUS_ONE_SLOT = 11;
    public static final int QUANTITY_SLOT = 12;
    public static final int PLUS_ONE_SLOT = 15;
    public static final int PLUS_LARGE_SLOT = 16;
    public static final int CONFIRM_SLOT = 22;
    public static final int RETURN_SLOT = 26;
    private static final int INVENTORY_SIZE = 27;
    private static final int MAX_QUANTITY = 2304;
    private static final String PREFIX = ChatColor.GOLD + "[赫朝经济] " + ChatColor.WHITE;

    private final HechaoEconomyPlugin plugin;
    private final QuarantinedSaleStore quarantinedSales;
    private final Map<UUID, Session> sessions = new ConcurrentHashMap<>();

    public ShopPurchaseMenu(
            HechaoEconomyPlugin plugin,
            QuarantinedSaleStore quarantinedSales) {
        this.plugin = plugin;
        this.quarantinedSales = quarantinedSales;
    }

    public void open(Player player, EconomyGateway.Product product) {
        closeExisting(player);
        if (product.shopUnitPrice() == null) {
            player.sendMessage(PREFIX + ChatColor.RED + "该物品暂未开放购买。");
            return;
        }
        var material = Material.matchMaterial(product.itemId());
        if (material == null || material.isAir()) {
            player.sendMessage(PREFIX + ChatColor.RED
                    + "当前服务端无法安全识别该商品，购买已取消。");
            return;
        }
        var inventory = Bukkit.createInventory(null, INVENTORY_SIZE, TITLE);
        var session = new Session(inventory, product, material);
        sessions.put(player.getUniqueId(), session);
        render(session, null);
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
        int slot = event.getRawSlot();
        if (slot < 0 || slot >= INVENTORY_SIZE || session.busy) {
            event.setCancelled(true);
            return;
        }
        event.setCancelled(true);
        switch (slot) {
            case MINUS_LARGE_SLOT -> adjust(session, -64);
            case MINUS_ONE_SLOT -> adjust(session, -1);
            case PLUS_ONE_SLOT -> adjust(session, 1);
            case PLUS_LARGE_SLOT -> adjust(session, 64);
            case CONFIRM_SLOT -> purchase(player, session);
            case RETURN_SLOT -> {
                sessions.remove(player.getUniqueId(), session);
                player.closeInventory();
                Bukkit.getScheduler().runTask(plugin,
                        () -> player.performCommand("shop"));
            }
            default -> {
            }
        }
    }

    @EventHandler
    public void onClose(InventoryCloseEvent event) {
        var session = sessions.get(event.getPlayer().getUniqueId());
        if (session != null && event.getView().getTopInventory() == session.inventory) {
            sessions.remove(event.getPlayer().getUniqueId(), session);
        }
    }

    private void adjust(Session session, int amount) {
        session.quantity = Math.max(
                1,
                Math.min(MAX_QUANTITY, session.quantity + amount));
        render(session, null);
    }

    private void purchase(Player player, Session session) {
        if (session.product.shopUnitPrice() == null) {
            render(session, "该商品已停止销售");
            return;
        }
        session.busy = true;
        UUID operationId = UUID.randomUUID();
        render(session, "正在提交购买请求");
        async(
                () -> plugin.gateway().shopPurchase(
                        "shop-buy:" + operationId,
                        player.getUniqueId(),
                        session.product.itemId(),
                        session.quantity),
                response -> {
                    if (!"Applied".equals(response.status())) {
                        session.busy = false;
                        render(session, failureMessage(response.failureCode()));
                        player.sendMessage(PREFIX + ChatColor.RED
                                + failureMessage(response.failureCode()) + "。");
                        return;
                    }
                    plugin.updateCachedBalance(
                            player.getUniqueId(), response.balance());
                    sessions.remove(player.getUniqueId(), session);
                    player.closeInventory();
                    player.sendMessage(PREFIX + "购买成功，支付 "
                            + money(response.totalAmount())
                            + "，物品已进入商城待领取。");
                    Bukkit.getScheduler().runTask(plugin,
                            () -> player.performCommand("shop claim"));
                },
                exception -> {
                    if (exception.isOutcomeUnknown()) {
                        sessions.remove(player.getUniqueId(), session);
                        player.closeInventory();
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "购买结果暂时无法确认，请稍后打开 /shop claim 查看。");
                    } else {
                        session.busy = false;
                        render(session, "购买失败，请检查余额或商品状态");
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "购买失败，请检查余额或商品状态。");
                    }
                });
    }

    private void render(Session session, String status) {
        session.inventory.clear();
        var stack = new ItemStack(
                session.material,
                Math.min(session.quantity, session.material.getMaxStackSize()));
        var meta = stack.getItemMeta();
        meta.setLore(List.of(
                ChatColor.GOLD + "购买价: " + money(session.product.shopUnitPrice()),
                ChatColor.GRAY + "数量: " + session.quantity,
                ChatColor.GRAY + "合计: " + money(total(session)),
                ChatColor.DARK_GRAY + "购买后前往 /shop claim 领取"));
        stack.setItemMeta(meta);
        session.inventory.setItem(ITEM_SLOT, stack);
        session.inventory.setItem(
                MINUS_LARGE_SLOT,
                control(Material.RED_DYE, "-64", "减少 64 个"));
        session.inventory.setItem(
                MINUS_ONE_SLOT,
                control(Material.REDSTONE, "-1", "减少 1 个"));
        session.inventory.setItem(
                QUANTITY_SLOT,
                control(Material.PAPER, "数量 " + session.quantity, "当前购买数量"));
        session.inventory.setItem(
                PLUS_ONE_SLOT,
                control(Material.LIME_DYE, "+1", "增加 1 个"));
        session.inventory.setItem(
                PLUS_LARGE_SLOT,
                control(Material.EMERALD, "+64", "增加 64 个"));
        session.inventory.setItem(
                4,
                control(
                        status == null ? Material.GOLD_INGOT : Material.REDSTONE,
                        status == null ? "确认商品" : status,
                        status == null
                                ? "服务端会再次校验价格和余额"
                                : "请返回商城后重试"));
        session.inventory.setItem(
                CONFIRM_SLOT,
                control(
                        session.busy ? Material.GRAY_DYE : Material.LIME_DYE,
                        session.busy ? "正在处理" : "确认购买",
                        session.busy ? "请勿重复点击" : "扣除金币并生成待领取物品"));
        session.inventory.setItem(
                RETURN_SLOT,
                control(Material.BARRIER, "返回商城", "不购买并返回商城"));
    }

    private static ItemStack control(Material material, String name, String lore) {
        var stack = new ItemStack(material);
        var meta = stack.getItemMeta();
        meta.setDisplayName(ChatColor.WHITE + name);
        meta.setLore(List.of(ChatColor.GRAY + lore));
        stack.setItemMeta(meta);
        return stack;
    }

    private static BigDecimal total(Session session) {
        return session.product.shopUnitPrice()
                .multiply(BigDecimal.valueOf(session.quantity))
                .setScale(2, RoundingMode.HALF_UP);
    }

    private static String money(BigDecimal amount) {
        return amount.setScale(2, RoundingMode.HALF_UP).toPlainString() + " 金币";
    }

    private static String failureMessage(String code) {
        return switch (code == null ? "" : code) {
            case "INSUFFICIENT_FUNDS" -> "余额不足";
            case "PRODUCT_NOT_AVAILABLE" -> "商品暂未开放购买";
            default -> "经济服务拒绝了本次购买";
        };
    }

    private void closeExisting(Player player) {
        var existing = sessions.remove(player.getUniqueId());
        if (existing != null && player.getOpenInventory().getTopInventory() == existing.inventory) {
            player.closeInventory();
        }
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
        private final EconomyGateway.Product product;
        private final Material material;
        private int quantity = 1;
        private boolean busy;

        private Session(
                Inventory inventory,
                EconomyGateway.Product product,
                Material material) {
            this.inventory = inventory;
            this.product = product;
            this.material = material;
        }
    }
}

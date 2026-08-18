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
import org.bukkit.event.inventory.InventoryAction;
import org.bukkit.event.inventory.InventoryClickEvent;
import org.bukkit.event.inventory.InventoryCloseEvent;
import org.bukkit.event.inventory.InventoryDragEvent;
import org.bukkit.inventory.Inventory;
import org.bukkit.inventory.ItemStack;
import world.hechao.economy.HechaoEconomyPlugin;
import world.hechao.economy.api.EconomyGatewayException;
import world.hechao.economy.inventory.QuarantinedSaleStore;
import world.hechao.economy.inventory.SellItemPolicy;

public final class MarketListingMenu implements Listener {
    public static final String TITLE = "赫朝市场上架";
    public static final int INPUT_SLOT = 13;
    public static final int STATUS_SLOT = 4;
    public static final int CONFIRM_SLOT = 22;
    public static final int RETURN_SLOT = 26;

    private static final int INVENTORY_SIZE = 27;
    private static final String PREFIX = ChatColor.GOLD + "[赫朝经济] " + ChatColor.WHITE;

    private final HechaoEconomyPlugin plugin;
    private final QuarantinedSaleStore quarantinedSales;
    private final Map<UUID, Session> sessions = new ConcurrentHashMap<>();

    public MarketListingMenu(
            HechaoEconomyPlugin plugin,
            QuarantinedSaleStore quarantinedSales) {
        this.plugin = plugin;
        this.quarantinedSales = quarantinedSales;
    }

    public void open(Player player) {
        closeExisting(player);
        var inventory = Bukkit.createInventory(null, INVENTORY_SIZE, TITLE);
        var session = new Session(inventory);
        render(session, "放入要上架的普通物品", List.of(
                "输入总价后确认上架",
                "上架费为总价 1%，最低 1.00 金币"), false);
        inventory.setItem(
                RETURN_SLOT,
                item(Material.BARRIER, "返回", List.of("取回物品并返回玩家市场")));
        sessions.put(player.getUniqueId(), session);
        player.openInventory(inventory);
    }

    public String confirm(Player player, BigDecimal totalPrice) {
        var session = sessions.get(player.getUniqueId());
        if (session == null || !session.open) {
            return "请先在玩家市场中打开上架界面。";
        }
        if (session.busy) {
            return "上架请求正在处理中。";
        }
        if (totalPrice == null || totalPrice.signum() <= 0 || totalPrice.scale() > 2) {
            return "总价必须是大于 0 且最多两位小数的数字。";
        }
        if (totalPrice.compareTo(BigDecimal.ONE) < 0) {
            return "玩家市场最低总价为 1.00 金币。";
        }

        var stack = session.inventory.getItem(INPUT_SLOT);
        var validation = SellItemPolicy.validate(stack);
        if (!validation.allowed()) {
            return validation.reason();
        }
        var escrow = stack.clone();
        var operationId = UUID.randomUUID();
        session.inventory.setItem(INPUT_SLOT, null);
        session.busy = true;
        session.escrow = escrow;
        render(session, "正在提交挂单", List.of(
                validation.itemId() + " × " + escrow.getAmount(),
                "物品已进入服务端托管"), false);

        async(
                () -> plugin.gateway().marketCreate(
                        "market-list:" + operationId,
                        player.getUniqueId(),
                        player.getName(),
                        validation.itemId(),
                        escrow.getAmount(),
                        totalPrice),
                response -> {
                    if ("Applied".equals(response.status())) {
                        session.escrow = null;
                        session.busy = false;
                        plugin.updateCachedBalance(player.getUniqueId(), response.balance());
                        sessions.remove(player.getUniqueId(), session);
                        session.open = false;
                        player.closeInventory();
                        player.sendMessage(PREFIX + "上架成功，总价 "
                                + money(totalPrice) + "，上架费 "
                                + money(response.listingFee()) + "。");
                        Bukkit.getScheduler().runTask(plugin, () -> player.performCommand("ah mine"));
                    } else {
                        restore(player, session, operationId, validation.itemId(),
                                safeCode(response.failureCode()));
                    }
                },
                exception -> {
                    if (exception.isOutcomeUnknown()) {
                        quarantinedSales.add(
                                player.getUniqueId(),
                                operationId,
                                validation.itemId(),
                                escrow.getAmount(),
                                "MARKET_LIST_OUTCOME_UNKNOWN");
                        session.escrow = null;
                        session.busy = false;
                        if (session.open) {
                            render(session, "上架结果暂时无法确认", List.of(
                                    "物品已进入隔离记录，请联系管理员"), false);
                        } else {
                            sessions.remove(player.getUniqueId(), session);
                        }
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "上架结果暂时无法确认，物品已进入隔离记录。");
                    } else {
                        restore(player, session, operationId, validation.itemId(),
                                "DEFINITE_FAILURE");
                    }
                });
        return null;
    }

    public void closeAll() {
        for (var entry : List.copyOf(sessions.entrySet())) {
            var player = Bukkit.getPlayer(entry.getKey());
            if (player != null) {
                returnInput(player, entry.getValue());
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
        var session = session(player, event.getView().getTopInventory());
        if (session == null) {
            return;
        }
        int rawSlot = event.getRawSlot();
        if (rawSlot == RETURN_SLOT) {
            event.setCancelled(true);
            player.closeInventory();
            Bukkit.getScheduler().runTask(plugin, () -> player.performCommand("ah"));
            return;
        }
        if (rawSlot >= 0 && rawSlot < INVENTORY_SIZE && rawSlot != INPUT_SLOT) {
            event.setCancelled(true);
            return;
        }
        if (session.busy) {
            event.setCancelled(true);
            return;
        }
        if (rawSlot >= INVENTORY_SIZE && event.isShiftClick()) {
            event.setCancelled(true);
            shiftIntoInput(player, session, event);
            return;
        }
        if (event.getAction() == InventoryAction.COLLECT_TO_CURSOR
                || event.getAction() == InventoryAction.HOTBAR_SWAP
                || event.getAction() == InventoryAction.HOTBAR_MOVE_AND_READD) {
            event.setCancelled(true);
            return;
        }
        if (rawSlot == INPUT_SLOT && !event.getCursor().getType().isAir()) {
            var validation = SellItemPolicy.validate(event.getCursor());
            if (!validation.allowed()) {
                event.setCancelled(true);
                player.sendMessage(PREFIX + ChatColor.RED + validation.reason());
            }
        }
    }

    @EventHandler
    public void onDrag(InventoryDragEvent event) {
        if (!(event.getWhoClicked() instanceof Player player)) {
            return;
        }
        var session = session(player, event.getView().getTopInventory());
        if (session == null) {
            return;
        }
        if (session.busy
                || event.getRawSlots().stream()
                        .anyMatch(slot -> slot < INVENTORY_SIZE && slot != INPUT_SLOT)) {
            event.setCancelled(true);
            return;
        }
        if (event.getRawSlots().contains(INPUT_SLOT)) {
            var validation = SellItemPolicy.validate(event.getOldCursor());
            if (!validation.allowed()) {
                event.setCancelled(true);
                player.sendMessage(PREFIX + ChatColor.RED + validation.reason());
            }
        }
    }

    @EventHandler
    public void onClose(InventoryCloseEvent event) {
        if (!(event.getPlayer() instanceof Player player)) {
            return;
        }
        var session = session(player, event.getView().getTopInventory());
        if (session == null) {
            return;
        }
        session.open = false;
        returnInput(player, session);
        if (!session.busy) {
            sessions.remove(player.getUniqueId(), session);
        }
    }

    private void shiftIntoInput(
            Player player,
            Session session,
            InventoryClickEvent event) {
        var source = event.getCurrentItem();
        if (source == null || source.getType().isAir()) {
            return;
        }
        if (!empty(session.inventory.getItem(INPUT_SLOT))) {
            player.sendMessage(PREFIX + ChatColor.RED + "上架槽中已有物品。");
            return;
        }
        var validation = SellItemPolicy.validate(source);
        if (!validation.allowed()) {
            player.sendMessage(PREFIX + ChatColor.RED + validation.reason());
            return;
        }
        session.inventory.setItem(INPUT_SLOT, source.clone());
        event.setCurrentItem(null);
    }

    private void restore(
            Player player,
            Session session,
            UUID operationId,
            String itemId,
            String reason) {
        var escrow = session.escrow;
        session.escrow = null;
        session.busy = false;
        if (session.open && empty(session.inventory.getItem(INPUT_SLOT))) {
            session.inventory.setItem(INPUT_SLOT, escrow);
            render(session, "上架失败", List.of(
                    failureMessage(reason),
                    "物品已退回上架槽"), true);
        } else {
            returnStack(player, operationId, itemId, escrow, reason);
            sessions.remove(player.getUniqueId(), session);
        }
        player.sendMessage(PREFIX + ChatColor.RED + "上架失败，物品已退回。");
    }

    private void returnInput(Player player, Session session) {
        var input = session.inventory.getItem(INPUT_SLOT);
        if (empty(input)) {
            return;
        }
        session.inventory.setItem(INPUT_SLOT, null);
        String itemId = input.getType().getKey().toString();
        returnStack(player, UUID.randomUUID(), itemId, input, "MARKET_MENU_CLOSED");
    }

    private void returnStack(
            Player player,
            UUID operationId,
            String itemId,
            ItemStack stack,
            String reason) {
        if (empty(stack)) {
            return;
        }
        var leftovers = player.getInventory().addItem(stack);
        for (var leftover : leftovers.values()) {
            if (player.getItemOnCursor().getType().isAir()) {
                player.setItemOnCursor(leftover);
            } else {
                quarantinedSales.add(
                        player.getUniqueId(),
                        operationId,
                        itemId,
                        leftover.getAmount(),
                        reason);
                player.sendMessage(PREFIX + ChatColor.RED
                        + "背包空间不足，剩余物品已进入隔离记录。");
            }
        }
    }

    private void closeExisting(Player player) {
        var current = sessions.remove(player.getUniqueId());
        if (current != null) {
            current.open = false;
            returnInput(player, current);
        }
    }

    private Session session(Player player, Inventory inventory) {
        var session = sessions.get(player.getUniqueId());
        return session != null && session.inventory == inventory ? session : null;
    }

    private static void render(
            Session session,
            String heading,
            List<String> details,
            boolean error) {
        session.inventory.setItem(
                STATUS_SLOT,
                item(
                        error ? Material.REDSTONE : Material.GOLD_INGOT,
                        heading,
                        details));
        session.inventory.setItem(
                CONFIRM_SLOT,
                item(
                        session.busy ? Material.GRAY_DYE : Material.LIME_DYE,
                        session.busy ? "正在处理" : "输入总价后确认",
                        List.of("客户端会校验价格并提交")));
    }

    private static ItemStack item(Material material, String name, List<String> lore) {
        var stack = new ItemStack(material);
        var meta = stack.getItemMeta();
        meta.setDisplayName(ChatColor.WHITE + name);
        meta.setLore(lore.stream().map(line -> ChatColor.GRAY + line).toList());
        stack.setItemMeta(meta);
        return stack;
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

    private static boolean empty(ItemStack stack) {
        return stack == null || stack.getType().isAir() || stack.getAmount() < 1;
    }

    private static String money(BigDecimal amount) {
        return amount.setScale(2, RoundingMode.HALF_UP).toPlainString() + " 金币";
    }

    private static String safeCode(String value) {
        return value == null || value.isBlank() ? "UNKNOWN" : value;
    }

    private static String failureMessage(String code) {
        return switch (safeCode(code)) {
            case "ACTIVE_LISTING_LIMIT" -> "活动挂单已达到 5 个上限";
            case "INSUFFICIENT_LISTING_FEE" -> "余额不足以支付上架费";
            default -> "经济服务拒绝了本次上架";
        };
    }

    @FunctionalInterface
    private interface CheckedSupplier<T> {
        T get() throws EconomyGatewayException;
    }

    private static final class Session {
        private final Inventory inventory;
        private ItemStack escrow;
        private boolean busy;
        private boolean open = true;

        private Session(Inventory inventory) {
            this.inventory = inventory;
        }
    }
}

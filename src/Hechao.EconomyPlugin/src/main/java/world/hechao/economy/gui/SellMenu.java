package world.hechao.economy.gui;

import java.math.RoundingMode;
import java.time.Instant;
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
import world.hechao.economy.api.EconomyGateway;
import world.hechao.economy.api.EconomyGatewayException;
import world.hechao.economy.inventory.QuarantinedSaleStore;
import world.hechao.economy.inventory.SellItemPolicy;

public final class SellMenu implements Listener {
    public static final String TITLE = "赫朝物品回收";
    public static final int INPUT_SLOT = 13;
    public static final int STATUS_SLOT = 4;
    public static final int CONFIRM_SLOT = 22;
    public static final int RETURN_SLOT = 26;

    private static final int INVENTORY_SIZE = 27;
    private static final String PREFIX = ChatColor.GOLD + "[赫朝经济] " + ChatColor.WHITE;

    private final HechaoEconomyPlugin plugin;
    private final QuarantinedSaleStore quarantinedSales;
    private final Map<UUID, Session> openMenus = new ConcurrentHashMap<>();

    public SellMenu(
            HechaoEconomyPlugin plugin,
            QuarantinedSaleStore quarantinedSales) {
        this.plugin = plugin;
        this.quarantinedSales = quarantinedSales;
    }

    public void open(Player player) {
        closeExisting(player);
        var inventory = Bukkit.createInventory(null, INVENTORY_SIZE, TITLE);
        var session = new Session(inventory);
        fillFrame(session);
        render(session, State.EMPTY, "放入一组普通物品", List.of("系统会自动计算回收报价"));
        openMenus.put(player.getUniqueId(), session);
        player.openInventory(inventory);
    }

    public void closeAll() {
        for (var entry : List.copyOf(openMenus.entrySet())) {
            var player = Bukkit.getPlayer(entry.getKey());
            if (player != null) {
                returnInput(player, entry.getValue());
                player.closeInventory();
            }
            openMenus.remove(entry.getKey(), entry.getValue());
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
        if (rawSlot == CONFIRM_SLOT) {
            event.setCancelled(true);
            confirm(player, session);
            return;
        }
        if (rawSlot == RETURN_SLOT) {
            event.setCancelled(true);
            player.closeInventory();
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
        if (rawSlot == INPUT_SLOT) {
            var cursor = event.getCursor();
            if (!cursor.getType().isAir()) {
                var validation = SellItemPolicy.validate(cursor);
                if (!validation.allowed()) {
                    event.setCancelled(true);
                    reject(player, session, validation.reason());
                    return;
                }
            }
            scheduleRefresh(player, session);
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
                reject(player, session, validation.reason());
                return;
            }
            scheduleRefresh(player, session);
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
        session.revision++;
        returnInput(player, session);
        if (!session.busy) {
            openMenus.remove(player.getUniqueId(), session);
        }
    }

    static String quoteError(EconomyGatewayException exception) {
        return switch (exception.statusCode()) {
            case 404 -> "该物品未加入服务器回收目录。";
            case 409 -> "该物品当前额度不足或回收已暂停。";
            case 401, 403 -> "经济服务拒绝了当前服务器身份。";
            case 429 -> "请求过于频繁，请稍后再试。";
            default -> exception.isOutcomeUnknown()
                    ? "经济服务暂时无法响应，请稍后再试。"
                    : "经济服务拒绝了报价请求。";
        };
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
            reject(player, session, "出售槽中已有物品，请先取出或确认出售。");
            return;
        }
        var validation = SellItemPolicy.validate(source);
        if (!validation.allowed()) {
            reject(player, session, validation.reason());
            return;
        }
        session.inventory.setItem(INPUT_SLOT, source.clone());
        event.setCurrentItem(null);
        refreshQuote(player, session);
    }

    private void scheduleRefresh(Player player, Session session) {
        Bukkit.getScheduler().runTask(plugin, () -> {
            if (openMenus.get(player.getUniqueId()) == session && session.open) {
                refreshQuote(player, session);
            }
        });
    }

    private void refreshQuote(Player player, Session session) {
        int revision = ++session.revision;
        session.quote = null;
        session.snapshot = null;
        var stack = session.inventory.getItem(INPUT_SLOT);
        if (empty(stack)) {
            render(session, State.EMPTY, "放入一组普通物品", List.of(
                    "系统会自动计算回收报价"));
            return;
        }
        var validation = SellItemPolicy.validate(stack);
        if (!validation.allowed()) {
            reject(player, session, validation.reason());
            return;
        }

        var snapshot = stack.clone();
        render(session, State.QUOTING, "正在计算回收价", List.of(
                validation.itemId(),
                "数量: " + snapshot.getAmount()));
        async(
                () -> plugin.gateway().quote(
                        player.getUniqueId(),
                        validation.itemId(),
                        snapshot.getAmount()),
                quote -> {
                    if (!isCurrent(player, session, revision, snapshot)) {
                        return;
                    }
                    session.quote = quote;
                    session.snapshot = snapshot;
                    render(session, State.READY, "预计获得 " + money(quote.totalAmount()), List.of(
                            quote.itemId() + " × " + quote.quantity(),
                            "点击确认后才会扣除物品"));
                },
                exception -> {
                    if (!isCurrent(player, session, revision, snapshot)) {
                        return;
                    }
                    render(session, State.ERROR, quoteError(exception), List.of(
                            "取回物品或更换后重试"));
                });
    }

    private void confirm(Player player, Session session) {
        if (session.busy || session.quote == null || session.snapshot == null) {
            reject(player, session, "报价尚未完成，暂时不能确认出售。");
            return;
        }
        if (!session.quote.expiresAt().isAfter(Instant.now())) {
            refreshQuote(player, session);
            reject(player, session, "报价已过期，正在重新计算。");
            return;
        }
        var current = session.inventory.getItem(INPUT_SLOT);
        if (empty(current)
                || !current.isSimilar(session.snapshot)
                || current.getAmount() != session.quote.quantity()) {
            refreshQuote(player, session);
            reject(player, session, "物品已经变化，正在重新计算报价。");
            return;
        }

        var quote = session.quote;
        var escrow = current.clone();
        var operationId = UUID.randomUUID();
        session.inventory.setItem(INPUT_SLOT, null);
        session.busy = true;
        session.escrow = escrow;
        session.quote = null;
        session.snapshot = null;
        session.revision++;
        render(session, State.COMMITTING, "正在提交交易", List.of(
                "物品已进入服务端托管，请勿重复操作"));

        async(
                () -> plugin.gateway().commit(
                        "sale:" + operationId,
                        quote.quoteId(),
                        player.getUniqueId()),
                commit -> finishCommit(player, session, operationId, quote, commit),
                exception -> failCommit(player, session, operationId, quote, exception));
    }

    private void finishCommit(
            Player player,
            Session session,
            UUID operationId,
            EconomyGateway.SaleQuote quote,
            EconomyGateway.SaleCommit commit) {
        if ("Applied".equals(commit.status())) {
            plugin.updateCachedBalance(player.getUniqueId(), commit.balance());
            session.escrow = null;
            session.busy = false;
            if (session.open) {
                render(session, State.SUCCESS, "出售完成 · " + money(commit.amount()), List.of(
                        "当前余额: " + money(commit.balance()),
                        "可以继续放入下一组物品"));
            } else {
                openMenus.remove(player.getUniqueId(), session);
            }
            player.sendMessage(PREFIX + "出售成功，获得 " + money(commit.amount()) + "。");
            return;
        }
        restoreEscrow(player, session, operationId, quote.itemId(), "COMMIT_REJECTED");
    }

    private void failCommit(
            Player player,
            Session session,
            UUID operationId,
            EconomyGateway.SaleQuote quote,
            EconomyGatewayException exception) {
        if (exception.isOutcomeUnknown()) {
            quarantinedSales.add(
                    player.getUniqueId(),
                    operationId,
                    quote.itemId(),
                    quote.quantity(),
                    "OUTCOME_UNKNOWN");
            session.escrow = null;
            session.busy = false;
            if (session.open) {
                render(session, State.ERROR, "交易结果暂时无法确认", List.of(
                        "物品已进入隔离记录，请联系管理员"));
            } else {
                openMenus.remove(player.getUniqueId(), session);
            }
            player.sendMessage(PREFIX + ChatColor.RED
                    + "交易结果暂时无法确认，物品已进入隔离记录。");
            return;
        }
        restoreEscrow(player, session, operationId, quote.itemId(), "DEFINITE_FAILURE");
    }

    private void restoreEscrow(
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
            refreshQuote(player, session);
        } else {
            returnStack(player, operationId, itemId, escrow, reason);
            openMenus.remove(player.getUniqueId(), session);
        }
        player.sendMessage(PREFIX + ChatColor.RED + "出售未完成，物品已退回。");
    }

    private void reject(Player player, Session session, String message) {
        render(session, State.ERROR, message, List.of("只接受无名称、无附魔、无容器数据的物品"));
        player.sendMessage(PREFIX + ChatColor.RED + message);
    }

    private void returnInput(Player player, Session session) {
        var input = session.inventory.getItem(INPUT_SLOT);
        if (empty(input)) {
            return;
        }
        session.inventory.setItem(INPUT_SLOT, null);
        var validation = SellItemPolicy.validate(input);
        String itemId = validation.allowed() ? validation.itemId() : input.getType().getKey().toString();
        returnStack(player, UUID.randomUUID(), itemId, input, "MENU_CLOSED");
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
                continue;
            }
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

    private void closeExisting(Player player) {
        var current = openMenus.remove(player.getUniqueId());
        if (current != null) {
            current.open = false;
            current.revision++;
            returnInput(player, current);
        }
    }

    private Session session(Player player, Inventory inventory) {
        var session = openMenus.get(player.getUniqueId());
        return session != null && session.inventory == inventory ? session : null;
    }

    private boolean isCurrent(
            Player player,
            Session session,
            int revision,
            ItemStack snapshot) {
        var current = session.inventory.getItem(INPUT_SLOT);
        return openMenus.get(player.getUniqueId()) == session
                && session.open
                && !session.busy
                && session.revision == revision
                && !empty(current)
                && current.isSimilar(snapshot)
                && current.getAmount() == snapshot.getAmount();
    }

    private void fillFrame(Session session) {
        session.inventory.setItem(
                RETURN_SLOT,
                item(Material.BARRIER, "返回", List.of("关闭界面并取回未出售物品")));
    }

    private void render(
            Session session,
            State state,
            String heading,
            List<String> details) {
        Material statusMaterial = switch (state) {
            case EMPTY -> Material.HOPPER;
            case QUOTING, COMMITTING -> Material.CLOCK;
            case READY -> Material.GOLD_INGOT;
            case SUCCESS -> Material.EMERALD;
            case ERROR -> Material.REDSTONE;
        };
        var lore = details.stream().map(line -> ChatColor.GRAY + line).toList();
        session.inventory.setItem(STATUS_SLOT, item(statusMaterial, heading, lore));
        boolean ready = state == State.READY;
        session.inventory.setItem(
                CONFIRM_SLOT,
                item(
                        ready ? Material.LIME_DYE : Material.GRAY_DYE,
                        ready ? "确认出售" : "等待报价",
                        List.of(ready ? "点击提交本次交易" : "放入有效物品后自动报价")));
    }

    private static ItemStack item(Material material, String name, List<String> lore) {
        var stack = new ItemStack(material);
        var meta = stack.getItemMeta();
        meta.setDisplayName(ChatColor.WHITE + name);
        meta.setLore(lore);
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

    private static String money(java.math.BigDecimal amount) {
        return amount.setScale(2, RoundingMode.HALF_UP).toPlainString() + " 金币";
    }

    private enum State {
        EMPTY,
        QUOTING,
        READY,
        COMMITTING,
        SUCCESS,
        ERROR
    }

    @FunctionalInterface
    private interface CheckedSupplier<T> {
        T get() throws EconomyGatewayException;
    }

    private static final class Session {
        private final Inventory inventory;
        private EconomyGateway.SaleQuote quote;
        private ItemStack snapshot;
        private ItemStack escrow;
        private int revision;
        private boolean busy;
        private boolean open = true;

        private Session(Inventory inventory) {
            this.inventory = inventory;
        }
    }
}

package world.hechao.economy.gui;

import java.math.BigDecimal;
import java.math.RoundingMode;
import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
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

public final class MarketMenu implements Listener {
    public static final String MARKET_TITLE = "赫朝玩家市场";
    public static final String MINE_TITLE = "赫朝我的挂单";
    public static final String DELIVERY_TITLE = "赫朝待领取";
    public static final String PURCHASE_TITLE = "赫朝确认购买";
    public static final String CANCEL_TITLE = "赫朝确认下架";

    public static final int PAGE_SIZE = 45;
    public static final int CREATE_SLOT = 45;
    public static final int MINE_SLOT = 46;
    public static final int BROWSE_SLOT = 47;
    public static final int PREVIOUS_SLOT = 48;
    public static final int PAGE_INFO_SLOT = 49;
    public static final int NEXT_SLOT = 50;
    public static final int DELIVERY_SLOT = 52;
    public static final int RETURN_SLOT = 53;
    public static final int DECISION_ITEM_SLOT = 13;
    public static final int DECISION_CONFIRM_SLOT = 22;
    public static final int DECISION_RETURN_SLOT = 26;

    private static final int INVENTORY_SIZE = 54;
    private static final int DECISION_SIZE = 27;
    private static final String PREFIX = ChatColor.GOLD + "[赫朝经济] " + ChatColor.WHITE;

    private final HechaoEconomyPlugin plugin;
    private final MarketListingMenu listingMenu;
    private final QuarantinedSaleStore quarantinedSales;
    private final Map<UUID, Session> sessions = new ConcurrentHashMap<>();
    private final Map<UUID, Confirmation> confirmations = new ConcurrentHashMap<>();
    private final Set<UUID> claimingDeliveries = ConcurrentHashMap.newKeySet();

    public MarketMenu(
            HechaoEconomyPlugin plugin,
            MarketListingMenu listingMenu,
            QuarantinedSaleStore quarantinedSales) {
        this.plugin = plugin;
        this.listingMenu = listingMenu;
        this.quarantinedSales = quarantinedSales;
    }

    public void openBrowse(Player player, List<EconomyGateway.MarketListing> listings) {
        open(player, new Session(Mode.BROWSE, listings, List.of()));
    }

    public void openMine(Player player, List<EconomyGateway.MarketListing> listings) {
        open(player, new Session(Mode.MINE, listings, List.of()));
    }

    public void openDeliveries(
            Player player,
            List<EconomyGateway.MarketDelivery> deliveries) {
        open(player, new Session(Mode.DELIVERIES, List.of(), deliveries));
    }

    public boolean search(Player player, MarketSearchRequest search) {
        var session = sessions.get(player.getUniqueId());
        if (session == null) {
            return false;
        }
        session.query = search.query();
        session.translatedItemIds = search.translatedItemIds();
        session.page = 0;
        render(session);
        return true;
    }

    public void closeAll() {
        for (var playerId : new HashSet<>(sessions.keySet())) {
            var player = Bukkit.getPlayer(playerId);
            if (player != null) {
                player.closeInventory();
            }
        }
        sessions.clear();
        confirmations.clear();
        claimingDeliveries.clear();
    }

    @EventHandler
    public void onClick(InventoryClickEvent event) {
        if (!(event.getWhoClicked() instanceof Player player)) {
            return;
        }
        var confirmation = confirmations.get(player.getUniqueId());
        if (confirmation != null
                && event.getView().getTopInventory() == confirmation.inventory) {
            event.setCancelled(true);
            handleConfirmationClick(player, confirmation, event.getRawSlot());
            return;
        }

        var session = sessions.get(player.getUniqueId());
        if (session == null || event.getView().getTopInventory() != session.inventory) {
            return;
        }
        event.setCancelled(true);
        int slot = event.getRawSlot();
        if (slot >= 0 && slot < PAGE_SIZE) {
            if (session.mode == Mode.DELIVERIES) {
                var delivery = session.visibleDeliveries.get(slot);
                if (delivery != null) {
                    claim(player, session, delivery);
                }
            } else {
                var listing = session.visibleListings.get(slot);
                if (listing != null) {
                    openConfirmation(player, session, listing);
                }
            }
            return;
        }
        switch (slot) {
            case CREATE_SLOT -> listingMenu.open(player);
            case MINE_SLOT -> refreshMine(player);
            case BROWSE_SLOT -> refreshBrowse(player);
            case PREVIOUS_SLOT -> changePage(session, -1);
            case NEXT_SLOT -> changePage(session, 1);
            case DELIVERY_SLOT -> refreshDeliveries(player);
            case RETURN_SLOT -> returnHome(player);
            default -> {
            }
        }
    }

    @EventHandler
    public void onClose(InventoryCloseEvent event) {
        var playerId = event.getPlayer().getUniqueId();
        var confirmation = confirmations.get(playerId);
        if (confirmation != null
                && event.getView().getTopInventory() == confirmation.inventory) {
            confirmations.remove(playerId, confirmation);
            return;
        }
        var session = sessions.get(playerId);
        if (session != null && event.getView().getTopInventory() == session.inventory) {
            if (session.suspended) {
                session.suspended = false;
            } else {
                sessions.remove(playerId, session);
            }
        }
    }

    private void open(Player player, Session session) {
        confirmations.remove(player.getUniqueId());
        session.inventory = Bukkit.createInventory(null, INVENTORY_SIZE, session.mode.title);
        render(session);
        sessions.put(player.getUniqueId(), session);
        player.openInventory(session.inventory);
    }

    private void render(Session session) {
        session.inventory.clear();
        session.visibleListings.clear();
        session.visibleDeliveries.clear();
        if (session.mode == Mode.DELIVERIES) {
            renderDeliveries(session);
        } else {
            renderListings(session);
        }
        renderControls(session);
    }

    private void renderListings(Session session) {
        var filtered = session.listings.stream()
                .filter(listing -> MarketplaceSearch.matches(
                        session.query,
                        session.translatedItemIds,
                        listing.itemId(),
                        listing.sellerName()))
                .toList();
        session.page = clampPage(session.page, filtered.size());
        int first = session.page * PAGE_SIZE;
        int count = Math.min(PAGE_SIZE, filtered.size() - first);
        for (int slot = 0; slot < count; slot++) {
            var listing = filtered.get(first + slot);
            var stack = listingItem(listing, session.mode == Mode.MINE);
            if (stack != null) {
                session.inventory.setItem(slot, stack);
                session.visibleListings.put(slot, listing);
            }
        }
        session.filteredCount = filtered.size();
    }

    private void renderDeliveries(Session session) {
        var filtered = session.deliveries.stream()
                .filter(delivery -> MarketplaceSearch.matches(
                        session.query,
                        session.translatedItemIds,
                        delivery.itemId(),
                        ""))
                .toList();
        session.page = clampPage(session.page, filtered.size());
        int first = session.page * PAGE_SIZE;
        int count = Math.min(PAGE_SIZE, filtered.size() - first);
        for (int slot = 0; slot < count; slot++) {
            var delivery = filtered.get(first + slot);
            var stack = deliveryItem(delivery);
            if (stack != null) {
                session.inventory.setItem(slot, stack);
                session.visibleDeliveries.put(slot, delivery);
            }
        }
        session.filteredCount = filtered.size();
    }

    private void renderControls(Session session) {
        session.inventory.setItem(
                CREATE_SLOT,
                control(Material.CRAFTING_TABLE, "上架物品", "放入普通物品并填写总价"));
        session.inventory.setItem(
                MINE_SLOT,
                control(Material.BOOK, "我的挂单", "查看和下架活动挂单"));
        session.inventory.setItem(
                BROWSE_SLOT,
                control(Material.EMERALD, "玩家市场", "浏览其他玩家的活动挂单"));
        int pageCount = pageCount(session.filteredCount);
        if (session.page > 0) {
            session.inventory.setItem(
                    PREVIOUS_SLOT,
                    control(Material.ARROW, "上一页", "查看上一页"));
        }
        session.inventory.setItem(
                PAGE_INFO_SLOT,
                control(
                        Material.PAPER,
                        "第 " + (session.page + 1) + " / " + pageCount
                                + " 页 · 共 " + session.filteredCount + " 项",
                        session.query.isBlank()
                                ? "数据由经济服务实时提供"
                                : "当前搜索: " + session.query));
        if (session.page + 1 < pageCount) {
            session.inventory.setItem(
                    NEXT_SLOT,
                    control(Material.ARROW, "下一页", "查看下一页"));
        }
        session.inventory.setItem(
                DELIVERY_SLOT,
                control(Material.CHEST, "待领取", "购买、下架和到期物品在这里领取"));
        session.inventory.setItem(
                RETURN_SLOT,
                control(Material.BARRIER, "返回首页", "返回天域远征功能首页"));
    }

    private void openConfirmation(
            Player player,
            Session session,
            EconomyGateway.MarketListing listing) {
        boolean cancelling = session.mode == Mode.MINE;
        var inventory = Bukkit.createInventory(
                null,
                DECISION_SIZE,
                cancelling ? CANCEL_TITLE : PURCHASE_TITLE);
        var stack = listingItem(listing, cancelling);
        if (stack != null) {
            inventory.setItem(DECISION_ITEM_SLOT, stack);
        }
        inventory.setItem(
                DECISION_CONFIRM_SLOT,
                control(
                        cancelling ? Material.ORANGE_DYE : Material.LIME_DYE,
                        cancelling ? "确认下架" : "确认购买",
                        cancelling ? "上架费不会退回" : "物品会进入待领取"));
        inventory.setItem(
                DECISION_RETURN_SLOT,
                control(Material.BARRIER, "返回", "返回列表"));
        var confirmation = new Confirmation(inventory, session, listing, cancelling);
        session.suspended = true;
        confirmations.put(player.getUniqueId(), confirmation);
        player.openInventory(inventory);
    }

    private void handleConfirmationClick(
            Player player,
            Confirmation confirmation,
            int slot) {
        if (slot == DECISION_RETURN_SLOT) {
            confirmations.remove(player.getUniqueId(), confirmation);
            player.openInventory(confirmation.parent.inventory);
            return;
        }
        if (slot != DECISION_CONFIRM_SLOT || confirmation.busy) {
            return;
        }
        confirmation.busy = true;
        confirmation.inventory.setItem(
                DECISION_CONFIRM_SLOT,
                control(Material.GRAY_DYE, "正在处理", "请勿重复操作"));
        if (confirmation.cancelling) {
            cancel(player, confirmation);
        } else {
            purchase(player, confirmation);
        }
    }

    private void purchase(Player player, Confirmation confirmation) {
        var operationId = UUID.randomUUID();
        async(
                () -> plugin.gateway().marketPurchase(
                        "market-buy:" + operationId,
                        confirmation.listing.listingId(),
                        player.getUniqueId(),
                        player.getName()),
                response -> {
                    if ("Applied".equals(response.status())) {
                        plugin.updateCachedBalance(player.getUniqueId(), response.buyerBalance());
                        player.sendMessage(PREFIX + "购买成功，支付 "
                                + money(response.totalPrice()) + "，物品已进入待领取。");
                        refreshDeliveries(player);
                    } else {
                        rejectConfirmation(player, confirmation,
                                failureMessage(response.failureCode()));
                    }
                },
                exception -> {
                    if (exception.isOutcomeUnknown()) {
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "购买结果暂时无法确认，正在刷新待领取列表。");
                        refreshDeliveries(player);
                    } else {
                        rejectConfirmation(player, confirmation, "经济服务暂时无法完成购买");
                    }
                });
    }

    private void cancel(Player player, Confirmation confirmation) {
        var operationId = UUID.randomUUID();
        async(
                () -> plugin.gateway().marketCancel(
                        "market-cancel:" + operationId,
                        confirmation.listing.listingId(),
                        player.getUniqueId()),
                response -> {
                    if ("Applied".equals(response.status())) {
                        player.sendMessage(PREFIX + "挂单已下架，物品已进入待领取。");
                        refreshMine(player);
                    } else {
                        rejectConfirmation(player, confirmation,
                                failureMessage(response.failureCode()));
                    }
                },
                exception -> {
                    if (exception.isOutcomeUnknown()) {
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "下架结果暂时无法确认，正在刷新挂单。");
                        refreshMine(player);
                    } else {
                        rejectConfirmation(player, confirmation, "经济服务暂时无法完成下架");
                    }
                });
    }

    private void rejectConfirmation(
            Player player,
            Confirmation confirmation,
            String message) {
        confirmation.busy = false;
        confirmation.inventory.setItem(
                DECISION_CONFIRM_SLOT,
                control(Material.REDSTONE, message, "返回列表后刷新重试"));
        player.sendMessage(PREFIX + ChatColor.RED + message + "。");
    }

    private void claim(
            Player player,
            Session session,
            EconomyGateway.MarketDelivery delivery) {
        if (!claimingDeliveries.add(delivery.deliveryId())) {
            return;
        }
        var material = Material.matchMaterial(delivery.itemId());
        if (material == null || material.isAir()) {
            claimingDeliveries.remove(delivery.deliveryId());
            player.sendMessage(PREFIX + ChatColor.RED
                    + "当前客户端无法识别该物品，不能领取。");
            return;
        }
        if (!canFit(player, material, delivery.quantity())) {
            claimingDeliveries.remove(delivery.deliveryId());
            player.sendMessage(PREFIX + ChatColor.RED + "背包空间不足，请整理后再领取。");
            return;
        }

        var operationId = UUID.randomUUID();
        async(
                () -> plugin.gateway().marketClaim(
                        "market-claim:" + operationId,
                        delivery.deliveryId(),
                        player.getUniqueId()),
                response -> {
                    claimingDeliveries.remove(delivery.deliveryId());
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
                                "MARKET_DELIVERY_INVENTORY_RACE");
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
                    claimingDeliveries.remove(delivery.deliveryId());
                    if (exception.isOutcomeUnknown()) {
                        quarantinedSales.add(
                                player.getUniqueId(),
                                delivery.deliveryId(),
                                delivery.itemId(),
                                delivery.quantity(),
                                "MARKET_CLAIM_OUTCOME_UNKNOWN");
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "领取结果暂时无法确认，已写入隔离记录并刷新列表。");
                        refreshDeliveries(player);
                    } else {
                        player.sendMessage(PREFIX + ChatColor.RED
                                + "经济服务暂时无法完成领取。");
                    }
                });
    }

    private void refreshMine(Player player) {
        async(
                () -> plugin.gateway().ownMarketListings(player.getUniqueId()),
                listings -> openMine(player, listings),
                exception -> player.sendMessage(PREFIX + ChatColor.RED
                        + "我的挂单暂时无法读取。"));
    }

    private void refreshBrowse(Player player) {
        async(
                () -> plugin.gateway().marketListings(""),
                listings -> openBrowse(player, listings),
                exception -> player.sendMessage(PREFIX + ChatColor.RED
                        + "玩家市场暂时无法读取。"));
    }

    private void refreshDeliveries(Player player) {
        async(
                () -> plugin.gateway().marketDeliveries(player.getUniqueId()),
                deliveries -> openDeliveries(player, deliveries),
                exception -> player.sendMessage(PREFIX + ChatColor.RED
                        + "待领取列表暂时无法读取。"));
    }

    private void changePage(Session session, int direction) {
        int next = Math.max(
                0,
                Math.min(pageCount(session.filteredCount) - 1, session.page + direction));
        if (next != session.page) {
            session.page = next;
            render(session);
        }
    }

    private void returnHome(Player player) {
        player.closeInventory();
        Bukkit.getScheduler().runTask(plugin, () -> player.performCommand("hechaomenu economy"));
    }

    private static ItemStack listingItem(
            EconomyGateway.MarketListing listing,
            boolean ownListing) {
        var material = Material.matchMaterial(listing.itemId());
        if (material == null || material.isAir()) {
            return null;
        }
        var stack = new ItemStack(material, Math.min(listing.quantity(), material.getMaxStackSize()));
        var meta = stack.getItemMeta();
        meta.setLore(List.of(
                ChatColor.GOLD + "总价: " + money(listing.totalPrice()),
                ChatColor.GRAY + "数量: " + listing.quantity(),
                ChatColor.GRAY + "卖家: " + listing.sellerName(),
                ChatColor.DARK_GRAY + "剩余: " + remaining(listing.expiresAt()),
                ownListing
                        ? ChatColor.YELLOW + "点击下架"
                        : ChatColor.GREEN + "点击购买"));
        stack.setItemMeta(meta);
        return stack;
    }

    private static ItemStack deliveryItem(EconomyGateway.MarketDelivery delivery) {
        var material = Material.matchMaterial(delivery.itemId());
        if (material == null || material.isAir()) {
            return null;
        }
        var stack = new ItemStack(material, Math.min(delivery.quantity(), material.getMaxStackSize()));
        var meta = stack.getItemMeta();
        meta.setLore(List.of(
                ChatColor.GRAY + "数量: " + delivery.quantity(),
                ChatColor.GRAY + "来源: " + deliveryReason(delivery.reason()),
                ChatColor.GREEN + "点击领取"));
        stack.setItemMeta(meta);
        return stack;
    }

    private static ItemStack control(Material material, String name, String description) {
        var stack = new ItemStack(material);
        var meta = stack.getItemMeta();
        meta.setDisplayName(ChatColor.WHITE + name);
        meta.setLore(List.of(ChatColor.GRAY + description));
        stack.setItemMeta(meta);
        return stack;
    }

    private static boolean canFit(Player player, Material material, int quantity) {
        int capacity = 0;
        var prototype = new ItemStack(material);
        for (var stack : player.getInventory().getStorageContents()) {
            if (stack == null || stack.getType().isAir()) {
                capacity += material.getMaxStackSize();
            } else if (stack.isSimilar(prototype)) {
                capacity += Math.max(0, stack.getMaxStackSize() - stack.getAmount());
            }
            if (capacity >= quantity) {
                return true;
            }
        }
        return false;
    }

    private static int give(Player player, Material material, int quantity) {
        int remaining = quantity;
        int leftovers = 0;
        while (remaining > 0) {
            int amount = Math.min(remaining, material.getMaxStackSize());
            var notAdded = player.getInventory().addItem(new ItemStack(material, amount));
            leftovers += notAdded.values().stream().mapToInt(ItemStack::getAmount).sum();
            remaining -= amount;
        }
        return leftovers;
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

    private static int clampPage(int page, int count) {
        return Math.max(0, Math.min(page, pageCount(count) - 1));
    }

    private static int pageCount(int count) {
        return Math.max(1, (Math.max(0, count) + PAGE_SIZE - 1) / PAGE_SIZE);
    }

    private static String money(BigDecimal amount) {
        return amount.setScale(2, RoundingMode.HALF_UP).toPlainString() + " 金币";
    }

    private static String remaining(Instant expiresAt) {
        long minutes = Math.max(0, Duration.between(Instant.now(), expiresAt).toMinutes());
        return minutes >= 60
                ? (minutes / 60) + " 小时 " + (minutes % 60) + " 分"
                : minutes + " 分钟";
    }

    private static String deliveryReason(String reason) {
        return switch (reason) {
            case "Purchase" -> "购买所得";
            case "Cancelled" -> "主动下架";
            case "Expired" -> "挂单到期";
            default -> "玩家市场";
        };
    }

    private static String failureMessage(String code) {
        if (code == null) {
            return "市场操作失败";
        }
        return switch (code) {
            case "LISTING_NOT_FOUND" -> "挂单不存在";
            case "LISTING_EXPIRED" -> "挂单已经到期";
            case "LISTING_NOT_ACTIVE" -> "挂单已被购买或下架";
            case "CANNOT_BUY_OWN_LISTING" -> "不能购买自己的挂单";
            case "INSUFFICIENT_FUNDS" -> "余额不足";
            case "DELIVERY_NOT_FOUND" -> "待领取物品不存在";
            case "DELIVERY_ALREADY_CLAIMED" -> "物品已经领取";
            default -> "市场操作失败: " + code;
        };
    }

    @FunctionalInterface
    private interface CheckedSupplier<T> {
        T get() throws EconomyGatewayException;
    }

    private enum Mode {
        BROWSE(MARKET_TITLE),
        MINE(MINE_TITLE),
        DELIVERIES(DELIVERY_TITLE);

        private final String title;

        Mode(String title) {
            this.title = title;
        }
    }

    private static final class Session {
        private final Mode mode;
        private final List<EconomyGateway.MarketListing> listings;
        private List<EconomyGateway.MarketDelivery> deliveries;
        private final Map<Integer, EconomyGateway.MarketListing> visibleListings = new HashMap<>();
        private final Map<Integer, EconomyGateway.MarketDelivery> visibleDeliveries = new HashMap<>();
        private Inventory inventory;
        private String query = "";
        private Set<String> translatedItemIds = Set.of();
        private int page;
        private int filteredCount;
        private boolean suspended;

        private Session(
                Mode mode,
                List<EconomyGateway.MarketListing> listings,
                List<EconomyGateway.MarketDelivery> deliveries) {
            this.mode = mode;
            this.listings = List.copyOf(listings);
            this.deliveries = List.copyOf(deliveries);
        }
    }

    private static final class Confirmation {
        private final Inventory inventory;
        private final Session parent;
        private final EconomyGateway.MarketListing listing;
        private final boolean cancelling;
        private boolean busy;

        private Confirmation(
                Inventory inventory,
                Session parent,
                EconomyGateway.MarketListing listing,
                boolean cancelling) {
            this.inventory = inventory;
            this.parent = parent;
            this.listing = listing;
            this.cancelling = cancelling;
        }
    }
}

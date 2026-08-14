package world.hechao.economy.commands;

import java.math.BigDecimal;
import java.math.RoundingMode;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ConcurrentHashMap;
import java.util.function.Consumer;
import java.util.function.Supplier;
import org.bukkit.Bukkit;
import org.bukkit.ChatColor;
import org.bukkit.Material;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabCompleter;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.jetbrains.annotations.NotNull;
import org.jetbrains.annotations.Nullable;
import world.hechao.economy.HechaoEconomyPlugin;
import world.hechao.economy.api.EconomyGateway;
import world.hechao.economy.api.EconomyGatewayException;
import world.hechao.economy.gui.ShopMenu;
import world.hechao.economy.inventory.QuarantinedSaleStore;
import world.hechao.economy.inventory.SellItemPolicy;

public final class EconomyCommandRouter implements CommandExecutor, TabCompleter {
    private final HechaoEconomyPlugin plugin;
    private final ShopMenu shopMenu;
    private final QuarantinedSaleStore quarantinedSales;
    private final Map<UUID, PendingSale> pendingSales = new ConcurrentHashMap<>();

    public EconomyCommandRouter(
            HechaoEconomyPlugin plugin,
            ShopMenu shopMenu,
            QuarantinedSaleStore quarantinedSales) {
        this.plugin = plugin;
        this.shopMenu = shopMenu;
        this.quarantinedSales = quarantinedSales;
    }

    @Override
    public boolean onCommand(
            @NotNull CommandSender sender,
            @NotNull Command command,
            @NotNull String label,
            @NotNull String[] args) {
        return switch (command.getName().toLowerCase(java.util.Locale.ROOT)) {
            case "money" -> money(sender, args);
            case "pay" -> pay(sender, args);
            case "sell" -> sell(sender, args);
            case "shop" -> shop(sender);
            case "heco" -> admin(sender, args);
            default -> false;
        };
    }

    private boolean money(CommandSender sender, String[] args) {
        UUID target;
        String displayName;
        if (args.length == 0 && sender instanceof Player player) {
            target = player.getUniqueId();
            displayName = player.getName();
        } else if (args.length == 1 && sender.hasPermission("hechao.economy.balance.others")) {
            var resolved = plugin.resolvePlayerUuid(args[0]);
            if (resolved.isEmpty()) {
                error(sender, "找不到该玩家。");
                return true;
            }
            target = resolved.get();
            displayName = args[0];
        } else {
            error(sender, "用法: /money [玩家]");
            return true;
        }
        async(
                () -> plugin.gateway().balance(target),
                balance -> {
                    plugin.updateCachedBalance(target, balance.availableBalance());
                    info(sender, displayName + " 的余额: " + money(balance.availableBalance()));
                },
                exception -> gatewayError(sender, exception));
        return true;
    }

    private boolean pay(CommandSender sender, String[] args) {
        if (!(sender instanceof Player player)) {
            error(sender, "该命令只能由玩家执行。");
            return true;
        }
        if (!plugin.isTradingAvailable()) {
            unavailable(player);
            return true;
        }
        if (args.length < 2 || args.length > 3) {
            error(player, "用法: /pay <在线玩家> <金额> [confirm]");
            return true;
        }
        var recipient = Bukkit.getPlayerExact(args[0]);
        if (recipient == null || recipient.getUniqueId().equals(player.getUniqueId())) {
            error(player, "接收者必须是另一名在线玩家。");
            return true;
        }
        var amount = parseMoney(args[1]);
        if (amount == null) {
            error(player, "金额必须是大于 0 且最多两位小数的数字。");
            return true;
        }
        if (amount.compareTo(plugin.payConfirmThreshold()) >= 0
                && (args.length != 3 || !"confirm".equalsIgnoreCase(args[2]))) {
            info(player, "这是大额转账，请再次输入: /pay "
                    + recipient.getName() + " " + amount.toPlainString() + " confirm");
            return true;
        }
        var key = "pay:" + UUID.randomUUID();
        async(
                () -> plugin.gateway().transfer(
                        key,
                        player.getUniqueId(),
                        recipient.getUniqueId(),
                        amount,
                        "player payment"),
                transfer -> {
                    plugin.updateCachedBalance(player.getUniqueId(), transfer.senderBalance());
                    plugin.updateCachedBalance(recipient.getUniqueId(), transfer.recipientBalance());
                    if ("Applied".equals(transfer.status())) {
                        success(player, "已向 " + recipient.getName() + " 支付 " + money(amount));
                        success(recipient, "收到 " + player.getName() + " 支付的 " + money(amount));
                    } else {
                        error(player, "转账失败: " + safeCode(transfer.failureCode()));
                    }
                },
                exception -> gatewayError(player, exception));
        return true;
    }

    private boolean sell(CommandSender sender, String[] args) {
        if (!(sender instanceof Player player)) {
            error(sender, "该命令只能由玩家执行。");
            return true;
        }
        if (!plugin.isTradingAvailable()) {
            unavailable(player);
            return true;
        }
        if (args.length == 1 && "confirm".equalsIgnoreCase(args[0])) {
            return confirmSale(player);
        }
        if (args.length != 0) {
            error(player, "用法: /sell [confirm]");
            return true;
        }
        var stack = player.getInventory().getItemInMainHand();
        var validation = SellItemPolicy.validate(stack);
        if (!validation.allowed()) {
            error(player, validation.reason());
            return true;
        }
        int quantity = stack.getAmount();
        async(
                () -> plugin.gateway().quote(
                        player.getUniqueId(),
                        validation.itemId(),
                        quantity),
                quote -> {
                    pendingSales.put(
                            player.getUniqueId(),
                            new PendingSale(quote, Instant.now()));
                    info(player, "报价: " + quantity + " 个 " + validation.itemId()
                            + " = " + money(quote.totalAmount()));
                    info(player, "30 秒内输入 /sell confirm 完成出售。");
                },
                exception -> gatewayError(player, exception));
        return true;
    }

    private boolean confirmSale(Player player) {
        var pending = pendingSales.remove(player.getUniqueId());
        if (pending == null || pending.quote().expiresAt().isBefore(Instant.now())) {
            error(player, "报价不存在或已经过期，请重新执行 /sell。");
            return true;
        }
        var current = player.getInventory().getItemInMainHand();
        var validation = SellItemPolicy.validate(current);
        if (!validation.allowed()
                || !pending.quote().itemId().equals(validation.itemId())
                || current.getAmount() < pending.quote().quantity()) {
            error(player, "主手物品已变化，出售已取消。");
            return true;
        }
        int remaining = current.getAmount() - pending.quote().quantity();
        var replacement = new ItemStack(Material.AIR);
        if (remaining > 0) {
            replacement = current.clone();
            replacement.setAmount(remaining);
        }
        player.getInventory().setItemInMainHand(replacement);
        var operationKey = UUID.randomUUID();
        async(
                () -> plugin.gateway().commit(
                        "sale:" + operationKey,
                        pending.quote().quoteId(),
                        player.getUniqueId()),
                commit -> {
                    if ("Applied".equals(commit.status())) {
                        plugin.updateCachedBalance(player.getUniqueId(), commit.balance());
                        success(player, "出售成功，获得 " + money(commit.amount()));
                    } else {
                        restoreOrQuarantine(
                                player,
                                operationKey,
                                pending.quote().itemId(),
                                pending.quote().quantity(),
                                safeCode(commit.failureCode()));
                    }
                },
                exception -> {
                    if (exception.isOutcomeUnknown()) {
                        quarantinedSales.add(
                                player.getUniqueId(),
                                operationKey,
                                pending.quote().itemId(),
                                pending.quote().quantity(),
                                "OUTCOME_UNKNOWN");
                        error(player, "交易结果暂时无法确认，物品已进入隔离记录，请联系管理员。");
                    } else {
                        restoreOrQuarantine(
                                player,
                                operationKey,
                                pending.quote().itemId(),
                                pending.quote().quantity(),
                                "DEFINITE_FAILURE");
                    }
                });
        return true;
    }

    private void restoreOrQuarantine(
            Player player,
            UUID operationId,
            String itemId,
            int quantity,
            String reason) {
        var material = Material.matchMaterial(itemId);
        if (material == null) {
            quarantinedSales.add(
                    player.getUniqueId(), operationId, itemId, quantity, reason);
            error(player, "无法恢复物品，已写入隔离记录。");
            return;
        }
        var leftovers = player.getInventory().addItem(new ItemStack(material, quantity));
        int quarantined = leftovers.values().stream().mapToInt(ItemStack::getAmount).sum();
        if (quarantined > 0) {
            quarantinedSales.add(
                    player.getUniqueId(), operationId, itemId, quarantined, reason);
            error(player, "背包空间不足，剩余物品已写入隔离记录。");
        } else {
            error(player, "出售未完成，物品已退回背包。");
        }
    }

    private boolean shop(CommandSender sender) {
        if (!(sender instanceof Player player)) {
            error(sender, "该命令只能由玩家执行。");
            return true;
        }
        async(
                () -> plugin.gateway().products(false),
                products -> shopMenu.open(player, products),
                exception -> gatewayError(player, exception));
        return true;
    }

    private boolean admin(CommandSender sender, String[] args) {
        if (!sender.hasPermission("hechao.economy.admin")) {
            error(sender, "没有权限。");
            return true;
        }
        if (args.length == 1 && "health".equalsIgnoreCase(args[0])) {
            info(sender, "API 配置: " + plugin.gateway().isConfigured()
                    + " | Vault 权威: " + plugin.isTradingAvailable()
                    + " | 隔离交易: " + quarantinedSales.count());
            return true;
        }
        if (args.length == 1 && "reload".equalsIgnoreCase(args[0])) {
            boolean loaded = plugin.reloadEconomyConfiguration();
            plugin.verifyVaultOwnership();
            if (loaded) {
                success(sender, "经济配置已重新加载。");
            } else {
                error(sender, "配置无效，交易继续保持关闭。");
            }
            return true;
        }
        if (args.length == 1 && "menu".equalsIgnoreCase(args[0])
                && sender instanceof Player player) {
            player.performCommand("hechaomenu economy");
            return true;
        }
        if (!(sender instanceof Player player)) {
            error(sender, "商品快捷配置必须由游戏内玩家手持物品执行。");
            return true;
        }
        if (args.length >= 2 && "product".equalsIgnoreCase(args[0])) {
            return product(player, java.util.Arrays.copyOfRange(args, 1, args.length));
        }
        error(sender, "用法: /heco <health|menu|reload|product>");
        return true;
    }

    private boolean product(Player player, String[] args) {
        var validation = SellItemPolicy.validate(player.getInventory().getItemInMainHand());
        if (!validation.allowed()) {
            error(player, validation.reason());
            return true;
        }
        if (args.length == 0) {
            ProductAdminPrompt.send(player, validation.itemId());
            return true;
        }
        if (args.length >= 2 && "set".equalsIgnoreCase(args[0])) {
            var price = parseMoney(args[1]);
            if (price == null) {
                error(player, "价格必须是大于 0 且最多两位小数的数字。");
                return true;
            }
            int personal = args.length >= 3
                    ? parsePositiveInt(args[2])
                    : plugin.defaultPersonalDailyLimit();
            int server = args.length >= 4
                    ? parsePositiveInt(args[3])
                    : plugin.defaultServerDailyLimit();
            if (personal < 1 || server < personal) {
                error(player, "额度无效，全服额度不能小于个人额度。");
                return true;
            }
            async(
                    () -> plugin.gateway().upsertProduct(
                            validation.itemId(),
                            price,
                            personal,
                            server,
                            player.getUniqueId(),
                            player.getName()),
                    product -> success(player, "已启用 " + product.itemId()
                            + "，单价 " + money(product.unitPrice())),
                    exception -> gatewayError(player, exception));
            return true;
        }
        if (args.length == 1 && "remove".equalsIgnoreCase(args[0])) {
            async(
                    () -> {
                        plugin.gateway().disableProduct(
                                validation.itemId(),
                                player.getUniqueId(),
                                player.getName());
                        return validation.itemId();
                    },
                    itemId -> success(player, "已暂停回收 " + itemId),
                    exception -> gatewayError(player, exception));
            return true;
        }
        ProductAdminPrompt.send(player, validation.itemId());
        return true;
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

    private static BigDecimal parseMoney(String value) {
        try {
            var amount = new BigDecimal(value).setScale(2, RoundingMode.UNNECESSARY);
            return amount.signum() > 0 ? amount : null;
        } catch (NumberFormatException | ArithmeticException exception) {
            return null;
        }
    }

    private static int parsePositiveInt(String value) {
        try {
            int parsed = Integer.parseInt(value);
            return parsed > 0 ? parsed : -1;
        } catch (NumberFormatException exception) {
            return -1;
        }
    }

    private static String money(BigDecimal amount) {
        return amount.setScale(2, RoundingMode.HALF_UP).toPlainString() + " 金币";
    }

    private static String safeCode(String code) {
        return code == null || code.isBlank() ? "UNKNOWN" : code;
    }

    private static void unavailable(CommandSender sender) {
        error(sender, "经济服务当前不可交易，请联系管理员检查 API 与 Vault 状态。");
    }

    private static void gatewayError(CommandSender sender, EconomyGatewayException exception) {
        error(sender, exception.isOutcomeUnknown()
                ? "经济服务暂时无法确认请求结果，请稍后重试。"
                : "经济请求被拒绝（HTTP " + exception.statusCode() + "）。");
    }

    private static void success(CommandSender sender, String message) {
        sender.sendMessage(ChatColor.GREEN + "[赫朝经济] " + ChatColor.WHITE + message);
    }

    private static void info(CommandSender sender, String message) {
        sender.sendMessage(ChatColor.GOLD + "[赫朝经济] " + ChatColor.WHITE + message);
    }

    private static void error(CommandSender sender, String message) {
        sender.sendMessage(ChatColor.RED + "[赫朝经济] " + ChatColor.WHITE + message);
    }

    @Override
    public @Nullable List<String> onTabComplete(
            @NotNull CommandSender sender,
            @NotNull Command command,
            @NotNull String alias,
            @NotNull String[] args) {
        var options = new ArrayList<String>();
        if ("sell".equalsIgnoreCase(command.getName()) && args.length == 1) {
            options.add("confirm");
        } else if ("heco".equalsIgnoreCase(command.getName()) && args.length == 1) {
            options.addAll(List.of("health", "menu", "product", "reload"));
        } else if ("heco".equalsIgnoreCase(command.getName())
                && args.length == 2
                && "product".equalsIgnoreCase(args[0])) {
            options.addAll(List.of("set", "remove"));
        } else if ("pay".equalsIgnoreCase(command.getName()) && args.length == 1) {
            Bukkit.getOnlinePlayers().forEach(player -> options.add(player.getName()));
        }
        var prefix = args.length == 0 ? "" : args[args.length - 1].toLowerCase(java.util.Locale.ROOT);
        return options.stream()
                .filter(option -> option.toLowerCase(java.util.Locale.ROOT).startsWith(prefix))
                .toList();
    }

    @FunctionalInterface
    private interface CheckedSupplier<T> {
        T get() throws EconomyGatewayException;
    }

    private record PendingSale(EconomyGateway.SaleQuote quote, Instant createdAt) {
    }
}

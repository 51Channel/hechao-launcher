package world.hechao.economy;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.logging.Level;
import net.milkbowl.vault.economy.Economy;
import org.bukkit.Bukkit;
import org.bukkit.OfflinePlayer;
import org.bukkit.command.PluginCommand;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.server.ServerLoadEvent;
import org.bukkit.plugin.ServicePriority;
import org.bukkit.plugin.java.JavaPlugin;
import world.hechao.economy.api.EconomyGateway;
import world.hechao.economy.api.EconomyGatewayException;
import world.hechao.economy.api.HttpEconomyGateway;
import world.hechao.economy.api.UnavailableEconomyGateway;
import world.hechao.economy.commands.EconomyCommandRouter;
import world.hechao.economy.gui.ShopMenu;
import world.hechao.economy.inventory.QuarantinedSaleStore;
import world.hechao.economy.placeholder.HechaoBalanceExpansion;
import world.hechao.economy.vault.HechaoVaultEconomy;

public final class HechaoEconomyPlugin extends JavaPlugin implements Listener {
    private final AtomicBoolean vaultOwner = new AtomicBoolean(false);
    private ExecutorService executor;
    private volatile EconomyConfiguration configuration;
    private volatile EconomyGateway gateway;
    private volatile BalanceCache balanceCache;
    private HechaoVaultEconomy vaultProvider;
    private QuarantinedSaleStore quarantinedSales;
    private ShopMenu shopMenu;

    @Override
    public void onEnable() {
        saveDefaultConfig();
        executor = Executors.newVirtualThreadPerTaskExecutor();
        quarantinedSales = new QuarantinedSaleStore(
                getDataFolder().toPath().resolve("quarantined-sales.yml"),
                getLogger());
        if (!reloadEconomyConfiguration()) {
            configuration = EconomyConfiguration.failClosedDefaults();
            gateway = new UnavailableEconomyGateway();
            balanceCache = new BalanceCache(java.time.Duration.ofSeconds(15));
        }

        vaultProvider = new HechaoVaultEconomy(this);
        getServer().getServicesManager().register(
                Economy.class,
                vaultProvider,
                this,
                ServicePriority.Highest);
        shopMenu = new ShopMenu();
        getServer().getPluginManager().registerEvents(this, this);
        getServer().getPluginManager().registerEvents(shopMenu, this);

        var commands = new EconomyCommandRouter(this, shopMenu, quarantinedSales);
        registerCommand("money", commands);
        registerCommand("pay", commands);
        registerCommand("sell", commands);
        registerCommand("shop", commands);
        registerCommand("heco", commands);

        if (getServer().getPluginManager().isPluginEnabled("PlaceholderAPI")) {
            new HechaoBalanceExpansion(this).register();
        }
        getServer().getScheduler().runTask(this, this::verifyVaultOwnership);

        if (!gateway.isConfigured()) {
            getLogger().warning(
                    "Economy credentials are absent. Vault ownership remains protected, "
                            + "but all balance changes are fail-closed.");
        }
    }

    @Override
    public void onDisable() {
        vaultOwner.set(false);
        if (vaultProvider != null) {
            getServer().getServicesManager().unregister(Economy.class, vaultProvider);
        }
        if (executor != null) {
            executor.shutdownNow();
        }
    }

    @EventHandler
    public void onServerLoaded(ServerLoadEvent event) {
        verifyVaultOwnership();
    }

    public void verifyVaultOwnership() {
        var registration = getServer().getServicesManager().getRegistration(Economy.class);
        boolean owner = registration != null && registration.getProvider() == vaultProvider;
        vaultOwner.set(owner);
        if (!owner) {
            var provider = registration == null
                    ? "none"
                    : registration.getProvider().getName();
            getLogger().severe(
                    "Vault selected economy provider '" + provider
                            + "' instead of HechaoEconomy. New transactions are disabled.");
        }
    }

    public boolean reloadEconomyConfiguration() {
        reloadConfig();
        try {
            var loaded = EconomyConfiguration.load(
                    getConfig(),
                    Path.of(".").toAbsolutePath().normalize());
            configuration = loaded;
            gateway = loaded.isConfigured()
                    ? new HttpEconomyGateway(EconomyConfigurationView.from(loaded))
                    : new UnavailableEconomyGateway();
            balanceCache = new BalanceCache(loaded.balanceCacheLifetime());
            return true;
        } catch (IOException | IllegalArgumentException exception) {
            configuration = EconomyConfiguration.failClosedDefaults();
            gateway = new UnavailableEconomyGateway();
            balanceCache = new BalanceCache(configuration.balanceCacheLifetime());
            getLogger().log(Level.SEVERE, "Invalid HechaoEconomy configuration", exception);
            return false;
        }
    }

    public boolean isTradingAvailable() {
        return vaultOwner.get() && gateway.isConfigured();
    }

    public EconomyGateway gateway() {
        return gateway;
    }

    public EconomyConfigurationView configurationView() {
        return EconomyConfigurationView.from(configuration);
    }

    public java.math.BigDecimal payConfirmThreshold() {
        return configuration.payConfirmThreshold();
    }

    public int defaultPersonalDailyLimit() {
        return configuration.defaultPersonalDailyLimit();
    }

    public int defaultServerDailyLimit() {
        return configuration.defaultServerDailyLimit();
    }

    public ExecutorService executor() {
        return executor;
    }

    public void updateCachedBalance(UUID playerUuid, java.math.BigDecimal balance) {
        balanceCache.put(playerUuid, balance);
    }

    public Optional<java.math.BigDecimal> cachedBalance(UUID playerUuid) {
        return balanceCache.getAny(playerUuid);
    }

    public double vaultBalance(UUID playerUuid) {
        var cached = balanceCache.getFresh(playerUuid);
        if (cached.isPresent()) {
            return cached.get().doubleValue();
        }
        if (Bukkit.isPrimaryThread()) {
            executor.submit(() -> refreshBalance(playerUuid));
            return balanceCache.getAny(playerUuid).orElse(java.math.BigDecimal.ZERO).doubleValue();
        }
        return refreshBalance(playerUuid).doubleValue();
    }

    private java.math.BigDecimal refreshBalance(UUID playerUuid) {
        try {
            var balance = gateway.balance(playerUuid).availableBalance();
            balanceCache.put(playerUuid, balance);
            return balance;
        } catch (EconomyGatewayException exception) {
            return balanceCache.getAny(playerUuid).orElse(java.math.BigDecimal.ZERO);
        }
    }

    public Optional<UUID> resolvePlayerUuid(String name) {
        if (name == null || name.isBlank()) {
            return Optional.empty();
        }
        var online = Bukkit.getPlayerExact(name);
        if (online != null) {
            return Optional.of(online.getUniqueId());
        }
        @SuppressWarnings("deprecation")
        OfflinePlayer offline = Bukkit.getOfflinePlayer(name);
        return offline.hasPlayedBefore() ? Optional.of(offline.getUniqueId()) : Optional.empty();
    }

    private void registerCommand(String name, EconomyCommandRouter router) {
        PluginCommand command = getCommand(name);
        if (command == null) {
            throw new IllegalStateException("Command is missing from plugin.yml: " + name);
        }
        command.setExecutor(router);
        command.setTabCompleter(router);
    }
}

package world.hechao.economy.vault;

import java.util.List;
import net.milkbowl.vault.economy.Economy;
import net.milkbowl.vault.economy.EconomyResponse;
import org.bukkit.OfflinePlayer;
import world.hechao.economy.HechaoEconomyPlugin;

public final class HechaoVaultEconomy implements Economy {
    private final HechaoEconomyPlugin plugin;

    public HechaoVaultEconomy(HechaoEconomyPlugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public boolean isEnabled() {
        return plugin.isEnabled();
    }

    @Override
    public String getName() {
        return "HechaoEconomy";
    }

    @Override
    public boolean hasBankSupport() {
        return false;
    }

    @Override
    public int fractionalDigits() {
        return 2;
    }

    @Override
    public String format(double amount) {
        return String.format(java.util.Locale.ROOT, "%.2f 金币", amount);
    }

    @Override
    public String currencyNamePlural() {
        return "金币";
    }

    @Override
    public String currencyNameSingular() {
        return "金币";
    }

    @Override
    public boolean hasAccount(String playerName) {
        return plugin.resolvePlayerUuid(playerName).isPresent();
    }

    @Override
    public boolean hasAccount(OfflinePlayer player) {
        return player != null;
    }

    @Override
    public boolean hasAccount(String playerName, String worldName) {
        return hasAccount(playerName);
    }

    @Override
    public boolean hasAccount(OfflinePlayer player, String worldName) {
        return hasAccount(player);
    }

    @Override
    public double getBalance(String playerName) {
        return plugin.resolvePlayerUuid(playerName)
                .map(plugin::vaultBalance)
                .orElse(0.0);
    }

    @Override
    public double getBalance(OfflinePlayer player) {
        return player == null ? 0.0 : plugin.vaultBalance(player.getUniqueId());
    }

    @Override
    public double getBalance(String playerName, String world) {
        return getBalance(playerName);
    }

    @Override
    public double getBalance(OfflinePlayer player, String world) {
        return getBalance(player);
    }

    @Override
    public boolean has(String playerName, double amount) {
        return getBalance(playerName) >= amount;
    }

    @Override
    public boolean has(OfflinePlayer player, double amount) {
        return getBalance(player) >= amount;
    }

    @Override
    public boolean has(String playerName, String worldName, double amount) {
        return has(playerName, amount);
    }

    @Override
    public boolean has(OfflinePlayer player, String worldName, double amount) {
        return has(player, amount);
    }

    @Override
    public EconomyResponse withdrawPlayer(String playerName, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse withdrawPlayer(OfflinePlayer player, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse withdrawPlayer(String playerName, String worldName, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse withdrawPlayer(OfflinePlayer player, String worldName, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse depositPlayer(String playerName, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse depositPlayer(OfflinePlayer player, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse depositPlayer(String playerName, String worldName, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse depositPlayer(OfflinePlayer player, String worldName, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse createBank(String name, String player) {
        return unsupported(0);
    }

    @Override
    public EconomyResponse createBank(String name, OfflinePlayer player) {
        return unsupported(0);
    }

    @Override
    public EconomyResponse deleteBank(String name) {
        return unsupported(0);
    }

    @Override
    public EconomyResponse bankBalance(String name) {
        return unsupported(0);
    }

    @Override
    public EconomyResponse bankHas(String name, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse bankWithdraw(String name, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse bankDeposit(String name, double amount) {
        return unsupported(amount);
    }

    @Override
    public EconomyResponse isBankOwner(String name, String playerName) {
        return unsupported(0);
    }

    @Override
    public EconomyResponse isBankOwner(String name, OfflinePlayer player) {
        return unsupported(0);
    }

    @Override
    public EconomyResponse isBankMember(String name, String playerName) {
        return unsupported(0);
    }

    @Override
    public EconomyResponse isBankMember(String name, OfflinePlayer player) {
        return unsupported(0);
    }

    @Override
    public List<String> getBanks() {
        return List.of();
    }

    @Override
    public boolean createPlayerAccount(String playerName) {
        return hasAccount(playerName);
    }

    @Override
    public boolean createPlayerAccount(OfflinePlayer player) {
        return player != null;
    }

    @Override
    public boolean createPlayerAccount(String playerName, String worldName) {
        return createPlayerAccount(playerName);
    }

    @Override
    public boolean createPlayerAccount(OfflinePlayer player, String worldName) {
        return createPlayerAccount(player);
    }

    private EconomyResponse unsupported(double amount) {
        return new EconomyResponse(
                amount,
                0,
                EconomyResponse.ResponseType.NOT_IMPLEMENTED,
                "Only Hechao audited transactions may change balances.");
    }
}

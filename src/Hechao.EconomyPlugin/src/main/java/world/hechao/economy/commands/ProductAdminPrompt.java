package world.hechao.economy.commands;

import java.util.List;
import net.md_5.bungee.api.ChatColor;
import net.md_5.bungee.api.chat.ClickEvent;
import net.md_5.bungee.api.chat.HoverEvent;
import net.md_5.bungee.api.chat.TextComponent;
import net.md_5.bungee.api.chat.hover.content.Text;
import org.bukkit.entity.Player;

final class ProductAdminPrompt {
    private static final List<String> PRESET_PRICES = List.of(
            "1.00",
            "5.00",
            "10.00",
            "25.00",
            "50.00",
            "100.00");

    private ProductAdminPrompt() {
    }

    static void send(Player player, String itemId) {
        var heading = text("[赫朝经济] ", ChatColor.GOLD);
        heading.addExtra(text("正在配置主手物品 ", ChatColor.WHITE));
        heading.addExtra(text(itemId, ChatColor.AQUA));
        player.spigot().sendMessage(heading);

        var presets = text("常用单价: ", ChatColor.GRAY);
        for (var price : PRESET_PRICES) {
            presets.addExtra(" ");
            presets.addExtra(priceButton(price));
        }
        player.spigot().sendMessage(presets);

        var actions = text("更多操作: ", ChatColor.GRAY);
        actions.addExtra(actionButton(
                "[自定义价格/额度]",
                ClickEvent.Action.SUGGEST_COMMAND,
                "/hechaoeconomy:heco product set ",
                "补全单价，可继续填写个人日限和全服日限",
                ChatColor.AQUA));
        actions.addExtra(" ");
        actions.addExtra(actionButton(
                "[暂停回收]",
                ClickEvent.Action.RUN_COMMAND,
                "/hechaoeconomy:heco product remove",
                "立即暂停该物品回收",
                ChatColor.RED));
        player.spigot().sendMessage(actions);
    }

    static List<String> presetCommands() {
        return PRESET_PRICES.stream()
                .map(price -> "/hechaoeconomy:heco product set " + price)
                .toList();
    }

    private static TextComponent priceButton(String price) {
        return actionButton(
                "[" + price + "]",
                ClickEvent.Action.RUN_COMMAND,
                "/hechaoeconomy:heco product set " + price,
                "以 " + price + " 金币启用回收",
                ChatColor.GREEN);
    }

    private static TextComponent actionButton(
            String label,
            ClickEvent.Action action,
            String command,
            String hover,
            ChatColor color) {
        var button = text(label, color);
        button.setClickEvent(new ClickEvent(action, command));
        button.setHoverEvent(new HoverEvent(HoverEvent.Action.SHOW_TEXT, new Text(hover)));
        return button;
    }

    private static TextComponent text(String value, ChatColor color) {
        var component = new TextComponent(value);
        component.setColor(color);
        return component;
    }
}

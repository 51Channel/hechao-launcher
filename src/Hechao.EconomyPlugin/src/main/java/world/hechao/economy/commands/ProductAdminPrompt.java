package world.hechao.economy.commands;

import java.util.List;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.event.ClickEvent;
import net.kyori.adventure.text.event.HoverEvent;
import net.kyori.adventure.text.format.NamedTextColor;
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
        player.sendMessage(Component.text("[赫朝经济] ", NamedTextColor.GOLD)
                .append(Component.text("正在配置主手物品 ", NamedTextColor.WHITE))
                .append(Component.text(itemId, NamedTextColor.AQUA)));

        var presets = Component.text("常用单价: ", NamedTextColor.GRAY);
        for (var price : PRESET_PRICES) {
            presets = presets.append(Component.space()).append(priceButton(price));
        }
        player.sendMessage(presets);

        player.sendMessage(Component.text("更多操作: ", NamedTextColor.GRAY)
                .append(actionButton(
                        "[自定义价格/额度]",
                        ClickEvent.suggestCommand("/hechaoeconomy:heco product set "),
                        "补全单价，可继续填写个人日限和全服日限",
                        NamedTextColor.AQUA))
                .append(Component.space())
                .append(actionButton(
                        "[暂停回收]",
                        ClickEvent.runCommand("/hechaoeconomy:heco product remove"),
                        "立即暂停该物品回收",
                        NamedTextColor.RED)));
    }

    static List<String> presetCommands() {
        return PRESET_PRICES.stream()
                .map(price -> "/hechaoeconomy:heco product set " + price)
                .toList();
    }

    private static Component priceButton(String price) {
        return actionButton(
                "[" + price + "]",
                ClickEvent.runCommand("/hechaoeconomy:heco product set " + price),
                "以 " + price + " 金币启用回收",
                NamedTextColor.GREEN);
    }

    private static Component actionButton(
            String label,
            ClickEvent click,
            String hover,
            NamedTextColor color) {
        return Component.text(label, color)
                .clickEvent(click)
                .hoverEvent(HoverEvent.showText(Component.text(hover, NamedTextColor.GRAY)));
    }
}

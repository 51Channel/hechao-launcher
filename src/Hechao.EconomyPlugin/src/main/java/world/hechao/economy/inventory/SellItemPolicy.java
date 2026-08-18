package world.hechao.economy.inventory;

import java.util.Locale;
import java.util.Set;
import org.bukkit.inventory.ItemStack;

public final class SellItemPolicy {
    private static final Set<String> DENIED_MATERIALS = Set.of(
            "bundle",
            "chest",
            "barrel",
            "shulker_box",
            "decorated_pot",
            "written_book",
            "writable_book",
            "potion",
            "splash_potion",
            "lingering_potion",
            "tipped_arrow",
            "suspicious_stew",
            "filled_map",
            "firework_rocket",
            "firework_star");

    private SellItemPolicy() {
    }

    public static Validation validate(ItemStack stack) {
        if (stack == null) {
            return new Validation(false, null, "出售槽中没有物品。");
        }
        var key = stack.getType().getKey();
        return evaluate(
                key.getNamespace(),
                key.getKey(),
                stack.hasItemMeta(),
                stack.getType().isAir(),
                stack.getAmount());
    }

    static Validation evaluate(
            String namespace,
            String materialKey,
            boolean hasItemMeta,
            boolean air,
            int amount) {
        if (air || amount < 1) {
            return new Validation(false, null, "出售槽中没有物品。");
        }
        if (hasItemMeta) {
            return new Validation(false, null, "带名称、附魔、容器或其他数据的物品不能出售。");
        }
        var material = materialKey.toLowerCase(Locale.ROOT);
        if (DENIED_MATERIALS.stream().anyMatch(material::endsWith)) {
            return new Validation(false, null, "容器和带内容数据的物品不能出售。");
        }
        return new Validation(
                true,
                namespace.toLowerCase(Locale.ROOT) + ":" + material,
                null);
    }

    public record Validation(boolean allowed, String itemId, String reason) {
    }
}

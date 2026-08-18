package world.hechao.economyscreen.client;

import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.List;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

final class EconomyMarketSearch {
    private EconomyMarketSearch() {
    }

    static Command encode(String query) {
        String value = query == null ? "" : query.trim();
        String encoded = value.isEmpty()
                ? "-"
                : Base64.getUrlEncoder().withoutPadding().encodeToString(
                        value.getBytes(StandardCharsets.UTF_8));
        var ids = translatedMatches(value);
        return new Command(
                encoded,
                ids.isEmpty() ? "-" : String.join(",", ids));
    }

    static List<String> translatedMatches(String query) {
        if (query == null || query.isBlank()) {
            return List.of();
        }
        return BuiltInRegistries.ITEM.stream()
                .filter(item -> item != Items.AIR)
                .map(ItemStack::new)
                .filter(stack -> EconomyCatalogSearch.matches(
                        query,
                        stack.getHoverName().getString(),
                        BuiltInRegistries.ITEM.getKey(stack.getItem()).toString()))
                .map(stack -> BuiltInRegistries.ITEM.getKey(stack.getItem()).toString())
                .distinct()
                .limit(24)
                .toList();
    }

    record Command(String encodedQuery, String encodedItemIds) {
        String marketCommand() {
            return "hechaoeconomy:ah search " + encodedQuery + " " + encodedItemIds;
        }

        String catalogCommand() {
            return "hechaoeconomy:shop search " + encodedQuery + " " + encodedItemIds;
        }
    }
}

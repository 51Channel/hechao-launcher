package world.hechao.economy.gui;

import java.util.Locale;
import java.util.Set;

final class MarketplaceSearch {
    private MarketplaceSearch() {
    }

    static boolean matches(
            String query,
            Set<String> translatedItemIds,
            String itemId,
            String sellerName) {
        String needle = normalize(query);
        if (needle.isEmpty()) {
            return true;
        }
        return translatedItemIds.contains(itemId)
                || fuzzyContains(normalize(itemId), needle)
                || fuzzyContains(normalize(sellerName), needle);
    }

    private static boolean fuzzyContains(String candidate, String query) {
        if (candidate.contains(query)) {
            return true;
        }
        int queryIndex = 0;
        for (int index = 0; index < candidate.length() && queryIndex < query.length(); index++) {
            if (candidate.charAt(index) == query.charAt(queryIndex)) {
                queryIndex++;
            }
        }
        return queryIndex == query.length();
    }

    private static String normalize(String value) {
        return value == null
                ? ""
                : value.toLowerCase(Locale.ROOT)
                        .replace(" ", "")
                        .replace("_", "")
                        .replace("-", "")
                        .replace(":", "");
    }
}

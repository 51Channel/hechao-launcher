package world.hechao.economyscreen.client;

import java.util.Locale;

final class EconomyCatalogSearch {
    private EconomyCatalogSearch() {
    }

    static boolean matches(String query, String displayName, String itemId) {
        String needle = normalize(query);
        if (needle.isEmpty()) {
            return true;
        }
        return fuzzyContains(normalize(displayName), needle)
                || fuzzyContains(normalize(itemId), needle);
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

package world.hechao.economy.gui;

import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.Set;
import java.util.regex.Pattern;

public record MarketSearchRequest(String query, Set<String> translatedItemIds) {
    private static final Pattern ITEM_ID = Pattern.compile(
            "^[a-z0-9_.-]{1,64}:[a-z0-9_./-]{1,96}$");

    public static MarketSearchRequest decode(String encodedQuery, String encodedItemIds) {
        String query = decodeQuery(encodedQuery);
        var ids = "-".equals(encodedItemIds)
                ? Set.<String>of()
                : java.util.Arrays.stream(encodedItemIds.split(","))
                        .limit(24)
                        .filter(id -> ITEM_ID.matcher(id).matches())
                        .collect(java.util.stream.Collectors.toUnmodifiableSet());
        return new MarketSearchRequest(query, ids);
    }

    private static String decodeQuery(String encoded) {
        if ("-".equals(encoded)) {
            return "";
        }
        try {
            String value = new String(
                    Base64.getUrlDecoder().decode(encoded),
                    StandardCharsets.UTF_8).trim();
            if (value.length() > 48 || value.chars().anyMatch(Character::isISOControl)) {
                throw new IllegalArgumentException("invalid market search query");
            }
            return value;
        } catch (IllegalArgumentException exception) {
            throw new IllegalArgumentException("市场搜索参数无效。", exception);
        }
    }
}

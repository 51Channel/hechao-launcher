package world.hechao.economyscreen.client;

import java.util.regex.Pattern;

final class EconomyMarketServerPage {
    static final int ITEM_SLOTS = 45;
    static final int PREVIOUS_SLOT = 48;
    static final int PAGE_INFO_SLOT = 49;
    static final int NEXT_SLOT = 50;
    static final int SORT_SLOT = 51;
    static final String PREVIOUS_LABEL = "上一页";
    static final String NEXT_LABEL = "下一页";

    private static final Pattern PAGE_INFO = Pattern.compile(
            "第\\s+(\\d+)\\s+/\\s+(\\d+)\\s+页\\s+·\\s+共\\s+(\\d+)\\s+项");

    private EconomyMarketServerPage() {
    }

    static Info parse(String label, int fallbackItemCount) {
        var match = PAGE_INFO.matcher(label == null ? "" : label);
        if (!match.find()) {
            return new Info(1, 1, Math.max(0, fallbackItemCount));
        }
        int page = Integer.parseInt(match.group(1));
        int pageCount = Integer.parseInt(match.group(2));
        int total = Integer.parseInt(match.group(3));
        return page < 1 || pageCount < 1 || page > pageCount || total < 0
                ? new Info(1, 1, Math.max(0, fallbackItemCount))
                : new Info(page, pageCount, total);
    }

    record Info(int page, int pageCount, int totalItemCount) {
    }
}

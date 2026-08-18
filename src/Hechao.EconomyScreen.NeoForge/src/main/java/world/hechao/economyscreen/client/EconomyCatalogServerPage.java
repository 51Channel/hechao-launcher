package world.hechao.economyscreen.client;

import java.util.regex.Pattern;

final class EconomyCatalogServerPage {
    static final int PRODUCT_SLOTS = 45;
    static final int PREVIOUS_SLOT = 48;
    static final int PAGE_INFO_SLOT = 49;
    static final int NEXT_SLOT = 50;
    static final String PREVIOUS_LABEL = "上一批";
    static final String NEXT_LABEL = "下一批";

    private static final Pattern PAGE_INFO = Pattern.compile(
            "第\\s+(\\d+)\\s+/\\s+(\\d+)\\s+批\\s+·\\s+共\\s+(\\d+)\\s+项");

    private EconomyCatalogServerPage() {
    }

    static Info parse(String label, int fallbackItemCount) {
        var match = PAGE_INFO.matcher(label == null ? "" : label);
        if (!match.find()) {
            return new Info(1, 1, Math.max(0, fallbackItemCount));
        }

        int page = Integer.parseInt(match.group(1));
        int pageCount = Integer.parseInt(match.group(2));
        int totalItemCount = Integer.parseInt(match.group(3));
        if (page < 1 || pageCount < 1 || page > pageCount || totalItemCount < 0) {
            return new Info(1, 1, Math.max(0, fallbackItemCount));
        }
        return new Info(page, pageCount, totalItemCount);
    }

    record Info(int page, int pageCount, int totalItemCount) {
    }
}

package world.hechao.economyscreen.client;

final class EconomyCatalogLayout {
    private static final int PANEL_MAX_WIDTH = 440;
    private static final int PANEL_MAX_HEIGHT = 250;
    private static final int PANEL_MARGIN_X = 12;
    private static final int PANEL_MARGIN_Y = 10;
    private static final int CONTENT_MARGIN = 12;
    private static final int HEADER_HEIGHT = 38;
    private static final int FOOTER_HEIGHT = 34;
    static final int CARD_HEIGHT = 36;
    static final int CARD_GAP = 6;

    private EconomyCatalogLayout() {
    }

    static Layout calculate(int screenWidth, int screenHeight) {
        if (screenWidth < 160 || screenHeight < 120) {
            throw new IllegalArgumentException("screen is too small");
        }
        int panelWidth = Math.min(PANEL_MAX_WIDTH, screenWidth - PANEL_MARGIN_X * 2);
        int panelHeight = Math.min(PANEL_MAX_HEIGHT, screenHeight - PANEL_MARGIN_Y * 2);
        int panelLeft = (screenWidth - panelWidth) / 2;
        int panelTop = (screenHeight - panelHeight) / 2;
        int contentLeft = panelLeft + CONTENT_MARGIN;
        int contentWidth = panelWidth - CONTENT_MARGIN * 2;
        int columns = contentWidth >= 360 ? 3 : contentWidth >= 236 ? 2 : 1;
        int cardWidth = (contentWidth - CARD_GAP * (columns - 1)) / columns;
        int contentTop = panelTop + HEADER_HEIGHT;
        int footerTop = panelTop + panelHeight - FOOTER_HEIGHT;
        int visibleRows = Math.max(
                1,
                (footerTop - contentTop + CARD_GAP)
                        / (CARD_HEIGHT + CARD_GAP));
        return new Layout(
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight,
                contentLeft,
                contentTop,
                contentWidth,
                columns,
                cardWidth,
                visibleRows,
                columns * visibleRows,
                footerTop);
    }

    static int maximumPage(int itemCount, int pageSize) {
        if (itemCount <= 0 || pageSize <= 0) {
            return 0;
        }
        return (itemCount - 1) / pageSize;
    }

    record Layout(
            int panelLeft,
            int panelTop,
            int panelWidth,
            int panelHeight,
            int contentLeft,
            int contentTop,
            int contentWidth,
            int columns,
            int cardWidth,
            int visibleRows,
            int pageSize,
            int footerTop) {
    }
}

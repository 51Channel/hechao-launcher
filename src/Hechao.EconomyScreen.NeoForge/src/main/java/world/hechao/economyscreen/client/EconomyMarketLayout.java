package world.hechao.economyscreen.client;

final class EconomyMarketLayout {
    static final int CARD_HEIGHT = 42;
    static final int CARD_GAP = 6;
    static final int BUTTON_HEIGHT = 20;

    private EconomyMarketLayout() {
    }

    static Layout calculate(int screenWidth, int screenHeight) {
        if (screenWidth < 160 || screenHeight < 120) {
            throw new IllegalArgumentException("screen is too small");
        }
        int width = Math.min(460, screenWidth - 24);
        int height = Math.min(260, screenHeight - 4);
        int left = (screenWidth - width) / 2;
        int top = (screenHeight - height) / 2;
        int contentWidth = width - 24;
        int columns = contentWidth >= 360 ? 3 : contentWidth >= 236 ? 2 : 1;
        int tabRows = 1;
        int tabColumns = 4;
        int tabGap = width >= 280 ? CARD_GAP : 2;
        int tabWidth = (contentWidth - tabGap * (tabColumns - 1)) / tabColumns;
        int tabsTop = top + 36;
        int contentTop = tabsTop + BUTTON_HEIGHT + 4;
        int footerTop = top + height - 28;
        int availableHeight = Math.max(22, footerTop - contentTop);
        int cardHeight = Math.min(CARD_HEIGHT, availableHeight);
        int rows = Math.max(
                1,
                (availableHeight + CARD_GAP) / (cardHeight + CARD_GAP));
        int cardWidth = (contentWidth - CARD_GAP * (columns - 1)) / columns;
        return new Layout(
                left,
                top,
                width,
                height,
                left + 12,
                contentTop,
                contentWidth,
                columns,
                cardWidth,
                cardHeight,
                rows,
                columns * rows,
                tabsTop,
                tabRows,
                tabColumns,
                tabWidth,
                tabGap,
                footerTop);
    }

    static int maximumPage(int itemCount, int pageSize) {
        return itemCount <= 0 || pageSize <= 0 ? 0 : (itemCount - 1) / pageSize;
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
            int cardHeight,
            int visibleRows,
            int pageSize,
            int tabsTop,
            int tabRows,
            int tabColumns,
            int tabWidth,
            int tabGap,
            int footerTop) {
    }
}

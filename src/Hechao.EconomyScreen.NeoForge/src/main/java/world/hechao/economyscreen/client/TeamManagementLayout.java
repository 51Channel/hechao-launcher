package world.hechao.economyscreen.client;

final class TeamManagementLayout {
    static final int BUTTON_HEIGHT = 20;
    static final int GAP = 6;

    private static final int PANEL_MAX_WIDTH = 460;
    private static final int PANEL_MAX_HEIGHT = 250;
    private static final int HORIZONTAL_MARGIN = 10;
    private static final int VERTICAL_MARGIN = 8;
    private static final int PADDING = 12;

    private TeamManagementLayout() {
    }

    static Layout calculate(int screenWidth, int screenHeight) {
        if (screenWidth <= 0 || screenHeight <= 0) {
            throw new IllegalArgumentException("screen dimensions must be positive");
        }
        int panelWidth = Math.min(PANEL_MAX_WIDTH, Math.max(1,
                screenWidth - HORIZONTAL_MARGIN * 2));
        int panelHeight = Math.min(PANEL_MAX_HEIGHT, Math.max(1,
                screenHeight - VERTICAL_MARGIN * 2));
        int panelLeft = (screenWidth - panelWidth) / 2;
        int panelTop = (screenHeight - panelHeight) / 2;
        int footerY = Math.max(
                panelTop + IndustrialUiTheme.HEADER_HEIGHT + 4,
                panelTop + panelHeight - BUTTON_HEIGHT - 10);
        int contentLeft = panelLeft + PADDING;
        int contentTop = panelTop + IndustrialUiTheme.HEADER_HEIGHT + 8;
        int contentWidth = Math.max(1, panelWidth - PADDING * 2);
        int contentHeight = Math.max(0, footerY - GAP - contentTop);
        boolean twoColumns = contentWidth >= 330 && contentHeight >= 104;
        int summaryWidth = twoColumns ? Math.min(164, contentWidth / 3 + 20) : contentWidth;
        int controlsRequiredHeight = BUTTON_HEIGHT * 3 + GAP * 2;
        int summaryHeight = twoColumns
                ? contentHeight
                : Math.max(0, Math.min(42,
                        contentHeight - controlsRequiredHeight - GAP));
        int controlsLeft = twoColumns ? contentLeft + summaryWidth + 10 : contentLeft;
        int controlsTop = twoColumns
                ? contentTop
                : contentTop + summaryHeight + (summaryHeight > 0 ? GAP : 0);
        int controlsWidth = twoColumns
                ? Math.max(1, contentWidth - summaryWidth - 10)
                : contentWidth;
        int controlsHeight = Math.max(0,
                contentTop + contentHeight - controlsTop);
        return new Layout(
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight,
                contentLeft,
                contentTop,
                contentWidth,
                contentHeight,
                summaryWidth,
                summaryHeight,
                controlsLeft,
                controlsTop,
                controlsWidth,
                controlsHeight,
                footerY,
                twoColumns);
    }

    record Layout(
            int panelLeft,
            int panelTop,
            int panelWidth,
            int panelHeight,
            int contentLeft,
            int contentTop,
            int contentWidth,
            int contentHeight,
            int summaryWidth,
            int summaryHeight,
            int controlsLeft,
            int controlsTop,
            int controlsWidth,
            int controlsHeight,
            int footerY,
            boolean twoColumns) {
    }
}

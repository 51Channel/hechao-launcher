package world.hechao.economyscreen.client;

final class EconomyResultLayout {
    static final int BUTTON_HEIGHT = 20;

    private static final int PANEL_MAX_WIDTH = 410;
    private static final int PANEL_MAX_HEIGHT = 196;
    private static final int BUTTON_MAX_WIDTH = 110;
    private static final int HORIZONTAL_MARGIN = 12;
    private static final int VERTICAL_MARGIN = 8;
    private static final int PANEL_PADDING = 12;
    private static final int BUTTON_GAP = 8;
    private static final int CONTENT_GAP = 7;
    private static final int DETAILED_MINIMUM_WIDTH = 240;
    private static final int DETAILED_MINIMUM_HEIGHT = 62;

    private EconomyResultLayout() {
    }

    static Layout calculate(int screenWidth, int screenHeight) {
        if (screenWidth <= 0 || screenHeight <= 0) {
            throw new IllegalArgumentException("screen dimensions must be positive");
        }

        int panelWidth = Math.min(
                PANEL_MAX_WIDTH,
                Math.max(1, screenWidth - HORIZONTAL_MARGIN * 2));
        int panelHeight = Math.min(
                PANEL_MAX_HEIGHT,
                Math.max(1, screenHeight - VERTICAL_MARGIN * 2));
        int panelLeft = (screenWidth - panelWidth) / 2;
        int panelTop = (screenHeight - panelHeight) / 2;
        int buttonY = Math.max(
                panelTop + IndustrialUiTheme.HEADER_HEIGHT + 2,
                panelTop + panelHeight - BUTTON_HEIGHT - 10);
        int buttonSpace = Math.max(1, panelWidth - PANEL_PADDING * 2 - BUTTON_GAP);
        int buttonWidth = Math.max(
                1,
                Math.min(BUTTON_MAX_WIDTH, buttonSpace / 2));
        int contentLeft = panelLeft + PANEL_PADDING;
        int contentTop = panelTop + IndustrialUiTheme.HEADER_HEIGHT + 8;
        int contentWidth = Math.max(1, panelWidth - PANEL_PADDING * 2);
        int contentHeight = Math.max(0, buttonY - CONTENT_GAP - contentTop);

        return new Layout(
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight,
                contentLeft,
                contentTop,
                contentWidth,
                contentHeight,
                buttonWidth,
                buttonY,
                contentWidth >= DETAILED_MINIMUM_WIDTH
                        && contentHeight >= DETAILED_MINIMUM_HEIGHT);
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
            int buttonWidth,
            int buttonY,
            boolean detailed) {
    }
}

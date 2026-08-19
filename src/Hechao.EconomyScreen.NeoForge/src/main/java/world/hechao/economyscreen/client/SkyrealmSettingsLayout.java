package world.hechao.economyscreen.client;

final class SkyrealmSettingsLayout {
    static final int TOGGLE_WIDTH = 66;
    static final int TOGGLE_HEIGHT = 20;
    static final int RETURN_WIDTH = 110;
    static final int RETURN_HEIGHT = 20;

    private static final int PANEL_MAX_WIDTH = 380;
    private static final int PANEL_MAX_HEIGHT = 218;
    private static final int HORIZONTAL_MARGIN = 8;
    private static final int VERTICAL_MARGIN = 4;
    private static final int CONTENT_PADDING = 12;
    private static final int ROW_GAP = 6;
    private static final int FOOTER_GAP = 10;
    private static final int FOOTER_PADDING = 10;

    private SkyrealmSettingsLayout() {
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
        int footerY = panelTop + panelHeight - RETURN_HEIGHT - FOOTER_PADDING;
        int rowsTop = panelTop + IndustrialUiTheme.HEADER_HEIGHT + 10;
        int rowsBottom = Math.max(rowsTop, footerY - FOOTER_GAP);
        int availableRows = Math.max(0, rowsBottom - rowsTop);
        int rowGap = availableRows >= 90 ? ROW_GAP : 2;
        int rowHeight = Math.max(
                1,
                (availableRows - rowGap * 2) / 3);

        return new Layout(
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight,
                panelLeft + CONTENT_PADDING,
                rowsTop,
                Math.max(1, panelWidth - CONTENT_PADDING * 2),
                rowHeight,
                rowGap,
                panelLeft + panelWidth - CONTENT_PADDING - TOGGLE_WIDTH,
                panelLeft + (panelWidth - RETURN_WIDTH) / 2,
                footerY);
    }

    record Layout(
            int panelLeft,
            int panelTop,
            int panelWidth,
            int panelHeight,
            int rowsLeft,
            int rowsTop,
            int rowWidth,
            int rowHeight,
            int rowGap,
            int toggleX,
            int returnX,
            int footerY) {
        int rowTop(int index) {
            return rowsTop + index * (rowHeight + rowGap);
        }

        int toggleY(int index) {
            return rowTop(index) + Math.max(0, (rowHeight - TOGGLE_HEIGHT) / 2);
        }
    }
}

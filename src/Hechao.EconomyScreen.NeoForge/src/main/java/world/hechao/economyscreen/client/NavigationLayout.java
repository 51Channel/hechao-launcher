package world.hechao.economyscreen.client;

final class NavigationLayout {
    static final int BUTTON_HEIGHT = 20;
    static final int COLUMN_GAP = 6;
    static final int ROW_GAP = 5;
    static final int ROW_STRIDE = BUTTON_HEIGHT + ROW_GAP;
    static final int NAVIGATION_GAP = 8;
    static final int NAVIGATION_HEIGHT = 20;

    private static final int TWO_COLUMN_MINIMUM_WIDTH = 430;
    private static final int MAXIMUM_GRID_WIDTH = 406;
    private static final int MINIMUM_TWO_COLUMN_GRID_WIDTH = 246;
    private static final int MAXIMUM_SINGLE_COLUMN_WIDTH = 200;
    private static final int SCREEN_MARGIN = 12;
    private static final int BOTTOM_MARGIN = 18;

    private NavigationLayout() {
    }

    static Layout calculate(
            int screenWidth,
            int screenHeight,
            int actionCount,
            int requestedScrollRow) {
        if (screenWidth <= 0 || screenHeight <= 0 || actionCount <= 0) {
            throw new IllegalArgumentException("screen dimensions and action count must be positive");
        }

        int columns = screenWidth >= TWO_COLUMN_MINIMUM_WIDTH ? 2 : 1;
        int gridWidth = columns == 2
                ? Math.min(
                        MAXIMUM_GRID_WIDTH,
                        Math.max(
                                MINIMUM_TWO_COLUMN_GRID_WIDTH,
                                (int) Math.floor(screenWidth * 0.63)))
                : Math.min(
                        MAXIMUM_SINGLE_COLUMN_WIDTH,
                        Math.max(120, screenWidth - SCREEN_MARGIN * 2));
        int buttonWidth = columns == 2
                ? (gridWidth - COLUMN_GAP) / 2
                : gridWidth;
        int totalRows = (actionCount + columns - 1) / columns;

        int titleTop = screenHeight < 120
                ? 4
                : Math.max(12, Math.min(24, screenHeight / 14));
        int contentTop = titleTop + (screenHeight < 120 ? 34 : 42);
        int bottomMargin = screenHeight < 120 ? 4 : BOTTOM_MARGIN;
        int availableHeight = Math.max(
                BUTTON_HEIGHT,
                screenHeight - contentTop - bottomMargin);
        int visibleRows = Math.min(
                totalRows,
                Math.max(1, (availableHeight + ROW_GAP) / ROW_STRIDE));
        boolean needsNavigation = totalRows > visibleRows;
        if (needsNavigation) {
            int gridHeightWithNavigation = Math.max(
                    BUTTON_HEIGHT,
                    availableHeight - NAVIGATION_GAP - NAVIGATION_HEIGHT);
            visibleRows = Math.min(
                    totalRows,
                    Math.max(1, (gridHeightWithNavigation + ROW_GAP) / ROW_STRIDE));
        }

        int maximumScrollRow = Math.max(0, totalRows - visibleRows);
        int scrollRow = Math.max(0, Math.min(maximumScrollRow, requestedScrollRow));
        int gridHeight = visibleRows * BUTTON_HEIGHT
                + Math.max(0, visibleRows - 1) * ROW_GAP;
        int blockHeight = gridHeight
                + (needsNavigation ? NAVIGATION_GAP + NAVIGATION_HEIGHT : 0);
        int gridTop = Math.max(contentTop, (screenHeight - blockHeight) / 2);
        int maximumGridTop = Math.max(4, screenHeight - bottomMargin - blockHeight);
        gridTop = Math.min(gridTop, maximumGridTop);
        int gridLeft = (screenWidth - gridWidth) / 2;
        int navigationTop = needsNavigation
                ? gridTop + gridHeight + NAVIGATION_GAP
                : -1;

        return new Layout(
                columns,
                totalRows,
                visibleRows,
                maximumScrollRow,
                scrollRow,
                gridLeft,
                gridTop,
                gridWidth,
                gridHeight,
                buttonWidth,
                titleTop,
                navigationTop,
                needsNavigation);
    }

    record Layout(
            int columns,
            int totalRows,
            int visibleRows,
            int maximumScrollRow,
            int scrollRow,
            int gridLeft,
            int gridTop,
            int gridWidth,
            int gridHeight,
            int buttonWidth,
            int titleTop,
            int navigationTop,
            boolean needsNavigation) {
    }
}

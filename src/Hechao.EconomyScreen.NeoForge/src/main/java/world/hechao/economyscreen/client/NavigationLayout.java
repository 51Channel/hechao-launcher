package world.hechao.economyscreen.client;

final class NavigationLayout {
    static final int BUTTON_HEIGHT = 26;
    static final int COLUMN_GAP = 6;
    static final int ROW_GAP = 6;
    static final int ROW_STRIDE = BUTTON_HEIGHT + ROW_GAP;
    static final int NAVIGATION_GAP = 6;
    static final int NAVIGATION_HEIGHT = 20;
    static final int RETURN_GAP = 7;
    static final int RETURN_HEIGHT = 20;

    private static final int THREE_COLUMN_MINIMUM_WIDTH = 430;
    private static final int TWO_COLUMN_MINIMUM_WIDTH = 260;
    private static final int MAXIMUM_THREE_COLUMN_GRID_WIDTH = 312;
    private static final int MAXIMUM_TWO_COLUMN_GRID_WIDTH = 218;
    private static final int MAXIMUM_SINGLE_COLUMN_WIDTH = 200;
    private static final int SCREEN_MARGIN = 12;
    private static final int COMPACT_SCREEN_MARGIN = 4;
    private static final int HEADER_HEIGHT = 22;
    private static final int HEADER_GAP = 7;

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

        int columns = screenWidth >= THREE_COLUMN_MINIMUM_WIDTH
                ? 3
                : screenWidth >= TWO_COLUMN_MINIMUM_WIDTH ? 2 : 1;
        int maximumGridWidth = switch (columns) {
            case 3 -> MAXIMUM_THREE_COLUMN_GRID_WIDTH;
            case 2 -> MAXIMUM_TWO_COLUMN_GRID_WIDTH;
            default -> MAXIMUM_SINGLE_COLUMN_WIDTH;
        };
        int margin = screenHeight < 120 ? COMPACT_SCREEN_MARGIN : SCREEN_MARGIN;
        int gridWidth = Math.min(
                maximumGridWidth,
                Math.max(120, screenWidth - margin * 2));
        int buttonWidth = (gridWidth - COLUMN_GAP * (columns - 1)) / columns;
        int totalRows = (actionCount + columns - 1) / columns;

        int outerMargin = screenHeight < 120 ? COMPACT_SCREEN_MARGIN : SCREEN_MARGIN;
        int availableHeight = Math.max(
                BUTTON_HEIGHT,
                screenHeight - outerMargin * 2 - HEADER_HEIGHT - HEADER_GAP
                        - RETURN_GAP - RETURN_HEIGHT);
        int visibleRows = Math.min(
                totalRows,
                Math.max(1, (availableHeight + ROW_GAP) / ROW_STRIDE));
        boolean needsNavigation = totalRows > visibleRows;
        boolean sharedFooter = needsNavigation && screenHeight < 130;
        if (needsNavigation && !sharedFooter) {
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
        int bodyHeight = gridHeight
                + (needsNavigation && !sharedFooter
                        ? NAVIGATION_GAP + NAVIGATION_HEIGHT
                        : 0)
                + RETURN_GAP
                + RETURN_HEIGHT;
        int blockHeight = HEADER_HEIGHT + HEADER_GAP + bodyHeight;
        int blockTop = Math.max(outerMargin, (screenHeight - blockHeight) / 2);
        int titleTop = blockTop;
        int gridTop = titleTop + HEADER_HEIGHT + HEADER_GAP;
        int gridLeft = (screenWidth - gridWidth) / 2;
        int navigationTop = needsNavigation
                ? gridTop + gridHeight + NAVIGATION_GAP
                : -1;
        int returnTop = sharedFooter
                ? navigationTop
                : (needsNavigation
                ? navigationTop + NAVIGATION_HEIGHT
                : gridTop + gridHeight) + RETURN_GAP;

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
                returnTop,
                sharedFooter,
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
            int returnTop,
            boolean sharedFooter,
            boolean needsNavigation) {
    }
}

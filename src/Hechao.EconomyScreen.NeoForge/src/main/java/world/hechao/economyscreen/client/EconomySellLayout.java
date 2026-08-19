package world.hechao.economyscreen.client;

final class EconomySellLayout {
    static final int BUTTON_WIDTH = 82;
    static final int BUTTON_HEIGHT = 20;

    private static final int PANEL_PADDING = 4;
    private static final int HEADER_EXTENSION = 38;
    private static final int FOOTER_GAP = 6;
    private static final int FOOTER_PADDING = 6;
    private static final int SIDE_GAP = 10;

    private EconomySellLayout() {
    }

    static Layout calculate(
            int screenWidth,
            int screenHeight,
            int containerLeft,
            int containerTop,
            int containerWidth,
            int containerHeight) {
        if (screenWidth <= 0
                || screenHeight <= 0
                || containerWidth <= 0
                || containerHeight <= 0) {
            throw new IllegalArgumentException("screen and container dimensions must be positive");
        }

        int availableHeader = Math.max(0, containerTop - PANEL_PADDING);
        int headerExtension = Math.min(HEADER_EXTENSION, availableHeader);
        boolean expandedHeader = headerExtension >= 30;
        int titleY = expandedHeader
                ? containerTop - headerExtension + 10
                : containerTop + 6;
        int statusY = expandedHeader ? titleY + 14 : -1;

        int panelLeft = containerLeft - PANEL_PADDING;
        int panelTop = containerTop - headerExtension;
        int panelRight = containerLeft + containerWidth + PANEL_PADDING;
        int panelBottom = containerTop + containerHeight + PANEL_PADDING;
        int confirmX = containerLeft + 5;
        int returnX = containerLeft + containerWidth - BUTTON_WIDTH - 5;
        int buttonY = -1;
        boolean customControls = false;

        int availableFooter = screenHeight - (containerTop + containerHeight);
        if (availableFooter >= BUTTON_HEIGHT + FOOTER_GAP + FOOTER_PADDING) {
            buttonY = containerTop + containerHeight + FOOTER_GAP;
            panelBottom = buttonY + BUTTON_HEIGHT + FOOTER_PADDING;
            customControls = true;
        } else {
            int sideSpace = screenWidth - containerWidth;
            int requiredSideSpace = (BUTTON_WIDTH + SIDE_GAP + PANEL_PADDING) * 2;
            if (sideSpace >= requiredSideSpace) {
                confirmX = containerLeft - SIDE_GAP - BUTTON_WIDTH;
                returnX = containerLeft + containerWidth + SIDE_GAP;
                buttonY = containerTop + containerHeight - BUTTON_HEIGHT - 5;
                panelLeft = confirmX - PANEL_PADDING;
                panelRight = returnX + BUTTON_WIDTH + PANEL_PADDING;
                customControls = true;
            }
        }

        return new Layout(
                panelLeft,
                panelTop,
                panelRight - panelLeft,
                panelBottom - panelTop,
                titleY,
                statusY,
                confirmX,
                returnX,
                buttonY,
                expandedHeader,
                customControls);
    }

    record Layout(
            int panelLeft,
            int panelTop,
            int panelWidth,
            int panelHeight,
            int titleY,
            int statusY,
            int confirmX,
            int returnX,
            int buttonY,
            boolean expandedHeader,
            boolean customControls) {
    }
}

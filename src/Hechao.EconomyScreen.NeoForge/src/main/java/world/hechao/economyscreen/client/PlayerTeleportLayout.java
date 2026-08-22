package world.hechao.economyscreen.client;

final class PlayerTeleportLayout {
    private static final int PANEL_MAX_WIDTH = 380;
    private static final int PANEL_MAX_HEIGHT = 202;

    private PlayerTeleportLayout() {
    }

    static Layout calculate(int screenWidth, int screenHeight) {
        if (screenWidth <= 0 || screenHeight <= 0) {
            throw new IllegalArgumentException("screen dimensions must be positive");
        }

        boolean compact = screenHeight < 210;
        int margin = compact ? 2 : 8;
        int padding = compact ? 4 : 12;
        int gap = compact ? 2 : 6;
        int fieldHeight = compact ? 14 : 18;
        int buttonHeight = compact ? 16 : 22;
        int footerHeight = compact ? 16 : 20;
        int headerHeight = compact ? 22 : IndustrialUiTheme.HEADER_HEIGHT;
        int panelWidth = Math.min(
                PANEL_MAX_WIDTH,
                Math.max(1, screenWidth - margin * 2));
        int panelHeight = Math.min(
                PANEL_MAX_HEIGHT,
                Math.max(1, screenHeight - margin * 2));
        int panelLeft = (screenWidth - panelWidth) / 2;
        int panelTop = (screenHeight - panelHeight) / 2;
        int contentLeft = panelLeft + padding;
        int contentWidth = Math.max(1, panelWidth - padding * 2);
        int fieldTop = panelTop + headerHeight + (compact ? 2 : 8);
        int footerY = panelTop + panelHeight - padding - footerHeight;

        int twoRowsHeight = buttonHeight * 2 + gap;
        int availableActionsHeight = Math.max(
                buttonHeight,
                footerY - gap - (fieldTop + fieldHeight + gap));
        int minimumStatusHeight = compact ? 16 : 24;
        boolean singleActionRow = availableActionsHeight
                < twoRowsHeight + gap + minimumStatusHeight;
        int columns = singleActionRow ? 4 : 2;
        int buttonGapWidth = gap * (columns - 1);
        int buttonWidth = Math.max(1, (contentWidth - buttonGapWidth) / columns);
        int actionsTop = fieldTop + fieldHeight + gap;
        int actionRowsHeight = singleActionRow ? buttonHeight : twoRowsHeight;
        int statusTop = actionsTop + actionRowsHeight + gap;
        int statusHeight = Math.max(0, footerY - gap - statusTop);
        int returnWidth = Math.min(100, contentWidth);

        return new Layout(
                panelLeft,
                panelTop,
                panelWidth,
                panelHeight,
                headerHeight,
                contentLeft,
                contentWidth,
                fieldTop,
                fieldHeight,
                actionsTop,
                buttonWidth,
                buttonHeight,
                gap,
                singleActionRow,
                contentLeft,
                statusTop,
                contentWidth,
                statusHeight,
                (screenWidth - returnWidth) / 2,
                footerY,
                returnWidth,
                footerHeight,
                compact);
    }

    record Layout(
            int panelLeft,
            int panelTop,
            int panelWidth,
            int panelHeight,
            int headerHeight,
            int contentLeft,
            int contentWidth,
            int fieldTop,
            int fieldHeight,
            int actionsTop,
            int buttonWidth,
            int buttonHeight,
            int gap,
            boolean singleActionRow,
            int statusLeft,
            int statusTop,
            int statusWidth,
            int statusHeight,
            int returnLeft,
            int returnY,
            int returnWidth,
            int returnHeight,
            boolean compact) {
        int actionX(int index) {
            int column = singleActionRow ? index : index % 2;
            return contentLeft + column * (buttonWidth + gap);
        }

        int actionY(int index) {
            int row = singleActionRow ? 0 : index / 2;
            return actionsTop + row * (buttonHeight + gap);
        }
    }
}

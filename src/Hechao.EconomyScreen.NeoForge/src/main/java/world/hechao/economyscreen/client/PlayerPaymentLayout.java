package world.hechao.economyscreen.client;

final class PlayerPaymentLayout {
    private static final int PANEL_MAX_WIDTH = 360;
    private static final int PANEL_MAX_HEIGHT = 190;

    private PlayerPaymentLayout() {
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
        int actionY = fieldTop + fieldHeight + gap;

        int fieldGap = Math.min(gap, Math.max(0, contentWidth - 2));
        int usableFieldsWidth = Math.max(2, contentWidth - fieldGap);
        int playerWidth = Math.max(1, usableFieldsWidth * 2 / 3);
        int amountWidth = Math.max(1, usableFieldsWidth - playerWidth);
        int actionWidth = Math.min(140, contentWidth);
        int returnWidth = Math.min(100, contentWidth);
        int statusTop = actionY + buttonHeight + gap;
        int statusHeight = Math.max(0, footerY - gap - statusTop);

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
                playerWidth,
                contentLeft + playerWidth + fieldGap,
                amountWidth,
                (screenWidth - actionWidth) / 2,
                actionY,
                actionWidth,
                buttonHeight,
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
            int playerWidth,
            int amountLeft,
            int amountWidth,
            int actionLeft,
            int actionY,
            int actionWidth,
            int buttonHeight,
            int statusLeft,
            int statusTop,
            int statusWidth,
            int statusHeight,
            int returnLeft,
            int returnY,
            int returnWidth,
            int returnHeight,
            boolean compact) {
    }
}

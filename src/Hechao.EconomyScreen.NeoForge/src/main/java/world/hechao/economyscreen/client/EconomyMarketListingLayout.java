package world.hechao.economyscreen.client;

final class EconomyMarketListingLayout {
    static final int IMAGE_WIDTH = 278;
    static final int IMAGE_HEIGHT = 166;

    private static final int PANEL_PADDING = 4;
    private static final int TOP_MODULE_TOP = 26;
    private static final int INVENTORY_MODULE_TOP = 80;
    private static final int LABEL_HEIGHT = 9;

    private EconomyMarketListingLayout() {
    }

    static Layout calculate(
            int imageWidth,
            int imageHeight,
            int inputSlotX,
            int inputSlotY) {
        if (imageWidth < IMAGE_WIDTH
                || imageHeight < IMAGE_HEIGHT
                || inputSlotX < 0
                || inputSlotY < 0) {
            throw new IllegalArgumentException("listing screen dimensions must be positive");
        }

        var panel = new Rect(
                -PANEL_PADDING,
                -PANEL_PADDING,
                imageWidth + PANEL_PADDING * 2,
                imageHeight + PANEL_PADDING * 2);
        var itemModule = new Rect(10, TOP_MODULE_TOP, 146, 42);
        var priceModule = new Rect(162, TOP_MODULE_TOP, 106, 49);
        var inventoryModule = new Rect(5, INVENTORY_MODULE_TOP, 170, 85);
        var guideModule = new Rect(181, INVENTORY_MODULE_TOP, 87, 85);
        var inputDock = new Rect(inputSlotX - 7, inputSlotY - 7, 30, 30);
        var priceField = new Rect(170, 46, 50, 18);
        var confirmButton = new Rect(224, 46, 36, 18);
        var returnButton = new Rect(imageWidth - 29, 6, 20, 20);

        if (itemModule.bottom() > inventoryModule.top()
                || priceModule.bottom() > inventoryModule.top()
                || priceField.overlaps(confirmButton)
                || !priceModule.contains(priceField)
                || !priceModule.contains(confirmButton)
                || !itemModule.contains(inputDock)
                || inventoryModule.bottom() > imageHeight
                || guideModule.bottom() > imageHeight) {
            throw new IllegalStateException("listing screen modules overlap or overflow");
        }

        return new Layout(
                panel,
                itemModule,
                priceModule,
                inventoryModule,
                guideModule,
                inputDock,
                priceField,
                confirmButton,
                returnButton,
                itemModule.left() + 8,
                itemModule.top() + 10,
                itemModule.top() + 28,
                priceModule.left() + 8,
                priceModule.top() + 10,
                guideModule.left() + 8,
                guideModule.top() + 10,
                guideModule.top() + 27,
                guideModule.top() + 43,
                guideModule.top() + 57,
                guideModule.top() + 71,
                INVENTORY_MODULE_TOP - LABEL_HEIGHT - 1,
                Math.max(24, inputSlotX - itemModule.left() - 18));
    }

    record Layout(
            Rect panel,
            Rect itemModule,
            Rect priceModule,
            Rect inventoryModule,
            Rect guideModule,
            Rect inputDock,
            Rect priceField,
            Rect confirmButton,
            Rect returnButton,
            int itemLabelX,
            int itemLabelY,
            int itemNameY,
            int priceLabelX,
            int priceLabelY,
            int guideLabelX,
            int guideLabelY,
            int guideStatusY,
            int guideMinimumY,
            int guideFeeY,
            int guidePromptY,
            int inventoryLabelY,
            int itemTextWidth) {
    }

    record Rect(int left, int top, int width, int height) {
        int right() {
            return left + width;
        }

        int bottom() {
            return top + height;
        }

        boolean contains(Rect other) {
            return other.left() >= left
                    && other.top() >= top
                    && other.right() <= right()
                    && other.bottom() <= bottom();
        }

        boolean overlaps(Rect other) {
            return left < other.right()
                    && right() > other.left()
                    && top < other.bottom()
                    && bottom() > other.top();
        }
    }
}

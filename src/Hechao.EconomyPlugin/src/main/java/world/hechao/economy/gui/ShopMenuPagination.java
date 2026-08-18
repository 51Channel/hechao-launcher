package world.hechao.economy.gui;

final class ShopMenuPagination {
    static final int INVENTORY_SIZE = 54;
    static final int PRODUCT_SLOTS = 45;
    static final int PREVIOUS_SLOT = 48;
    static final int PAGE_INFO_SLOT = 49;
    static final int NEXT_SLOT = 50;

    private ShopMenuPagination() {
    }

    static int pageCount(int itemCount) {
        return Math.max(1, (Math.max(0, itemCount) + PRODUCT_SLOTS - 1) / PRODUCT_SLOTS);
    }

    static int clampPage(int page, int itemCount) {
        return Math.max(0, Math.min(page, pageCount(itemCount) - 1));
    }

    static int firstProductIndex(int page, int itemCount) {
        return clampPage(page, itemCount) * PRODUCT_SLOTS;
    }

    static int productsOnPage(int page, int itemCount) {
        int first = firstProductIndex(page, itemCount);
        return Math.max(0, Math.min(PRODUCT_SLOTS, itemCount - first));
    }
}

package world.hechao.economy.gui;

record SellQuantityPlan(
        int requestedQuantity,
        int quotedQuantity,
        int remainingQuantity) {
    static SellQuantityPlan create(int requestedQuantity, int quotedQuantity) {
        if (requestedQuantity < 1) {
            throw new IllegalArgumentException("requested quantity must be positive");
        }
        if (quotedQuantity < 1 || quotedQuantity > requestedQuantity) {
            throw new IllegalArgumentException("quoted quantity must fit the requested stack");
        }
        return new SellQuantityPlan(
                requestedQuantity,
                quotedQuantity,
                requestedQuantity - quotedQuantity);
    }

    boolean partial() {
        return remainingQuantity > 0;
    }
}

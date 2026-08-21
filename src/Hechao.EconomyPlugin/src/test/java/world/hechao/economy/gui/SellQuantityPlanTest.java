package world.hechao.economy.gui;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class SellQuantityPlanTest {
    @Test
    void keepsTheUnquotedPartOfAStack() {
        var plan = SellQuantityPlan.create(64, 32);

        assertEquals(64, plan.requestedQuantity());
        assertEquals(32, plan.quotedQuantity());
        assertEquals(32, plan.remainingQuantity());
        assertTrue(plan.partial());
    }

    @Test
    void acceptsAFullStackQuoteWithoutRemainder() {
        var plan = SellQuantityPlan.create(16, 16);

        assertEquals(0, plan.remainingQuantity());
        assertFalse(plan.partial());
    }

    @Test
    void rejectsImpossibleQuoteQuantities() {
        assertThrows(IllegalArgumentException.class, () -> SellQuantityPlan.create(64, 0));
        assertThrows(IllegalArgumentException.class, () -> SellQuantityPlan.create(64, 65));
    }
}

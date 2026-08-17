package world.hechao.economy.commands;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

final class EconomyCommandRouterTest {
    @Test
    void bareProductSubcommandRoutesToProductHandler() {
        assertTrue(EconomyCommandRouter.isProductCommand(
                new String[] { "product" }));
        assertTrue(EconomyCommandRouter.isProductCommand(
                new String[] { "PRODUCT", "set", "5.00" }));
        assertFalse(EconomyCommandRouter.isProductCommand(new String[0]));
        assertFalse(EconomyCommandRouter.isProductCommand(
                new String[] { "health" }));
    }
}

package world.hechao.economy.commands;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
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

    @Test
    void balanceResponseKeepsLegacyLineAndAddsFrozenBalance() throws Exception {
        var source = Files.readString(Path.of(
                "src",
                "main",
                "java",
                "world",
                "hechao",
                "economy",
                "commands",
                "EconomyCommandRouter.java"));

        assertTrue(source.contains("displayName + \" 的余额: \""));
        assertTrue(source.contains("\"冻结余额: \" + money(balance.frozenBalance())"));
    }
}

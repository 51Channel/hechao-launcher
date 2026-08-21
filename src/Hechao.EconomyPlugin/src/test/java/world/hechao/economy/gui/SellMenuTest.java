package world.hechao.economy.gui;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import org.bukkit.inventory.Inventory;
import org.bukkit.inventory.ItemStack;
import org.junit.jupiter.api.Test;
import world.hechao.economy.api.EconomyGatewayException;

final class SellMenuTest {
    @Test
    void translatesMissingProductInsteadOfLeakingHttpStatus() {
        assertEquals(
                "该物品未加入服务器回收目录。",
                SellMenu.quoteError(new EconomyGatewayException(
                        "economy service returned HTTP 404",
                        false,
                        404)));
    }

    @Test
    void translatesUnknownOutcomeWithoutPromisingARefund() {
        assertEquals(
                "经济服务暂时无法响应，请稍后再试。",
                SellMenu.quoteError(new EconomyGatewayException(
                        "response lost",
                        true,
                        0)));
    }

    @Test
    void distinguishesProductAndQuotaConflictCodes() {
        assertEquals(
                "该物品的回收已暂停。",
                SellMenu.quoteError(new EconomyGatewayException(
                        "conflict",
                        false,
                        409,
                        "PRODUCT_DISABLED")));
        assertEquals(
                "该物品的个人今日回收额度已用完。",
                SellMenu.quoteError(new EconomyGatewayException(
                        "conflict",
                        false,
                        409,
                        "PERSONAL_LIMIT_EXCEEDED")));
        assertEquals(
                "该物品的全服今日回收额度已用完。",
                SellMenu.quoteError(new EconomyGatewayException(
                        "conflict",
                        false,
                        409,
                        "SERVER_LIMIT_EXCEEDED")));
    }

    @Test
    void definiteFailureMergesEscrowBackWithTheRetainedRemainder() {
        var inventory = mock(Inventory.class);
        var remainder = mock(ItemStack.class);
        var escrow = mock(ItemStack.class);
        when(inventory.getItem(SellMenu.INPUT_SLOT)).thenReturn(remainder);
        when(remainder.isSimilar(escrow)).thenReturn(true);
        when(remainder.getAmount()).thenReturn(32);
        when(escrow.getAmount()).thenReturn(32);
        when(remainder.getMaxStackSize()).thenReturn(64);

        assertTrue(SellMenu.mergeIntoInput(inventory, escrow));
        verify(remainder).setAmount(64);
        verify(inventory).setItem(SellMenu.INPUT_SLOT, remainder);
    }

    @Test
    void definiteFailureDoesNotOverfillAnUnmergeableInputStack() {
        var inventory = mock(Inventory.class);
        var remainder = mock(ItemStack.class);
        var escrow = mock(ItemStack.class);
        when(inventory.getItem(SellMenu.INPUT_SLOT)).thenReturn(remainder);
        when(remainder.isSimilar(escrow)).thenReturn(true);
        when(remainder.getAmount()).thenReturn(48);
        when(escrow.getAmount()).thenReturn(32);
        when(remainder.getMaxStackSize()).thenReturn(64);

        assertFalse(SellMenu.mergeIntoInput(inventory, escrow));
        verify(remainder, never()).setAmount(80);
        verify(inventory, never()).setItem(SellMenu.INPUT_SLOT, remainder);
    }
}

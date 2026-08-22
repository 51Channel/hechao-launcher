package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.math.BigDecimal;
import org.junit.jupiter.api.Test;

final class PlayerPaymentInputTest {
    @Test
    void acceptsPositiveMoneyWithAtMostTwoDecimalPlaces() {
        assertTrue(PlayerPaymentInput.acceptsAmount(""));
        assertTrue(PlayerPaymentInput.acceptsAmount("120"));
        assertTrue(PlayerPaymentInput.acceptsAmount("120.50"));
        assertFalse(PlayerPaymentInput.acceptsAmount("120.501"));
        assertFalse(PlayerPaymentInput.acceptsAmount("-1"));
        assertFalse(PlayerPaymentInput.acceptsAmount("一百"));

        assertEquals(new BigDecimal("120.50"), PlayerPaymentInput.parseAmount("120.50"));
        assertNull(PlayerPaymentInput.parseAmount("0"));
        assertNull(PlayerPaymentInput.parseAmount(""));
    }

    @Test
    void buildsOnlyStrictNamespacedConfirmedPaymentCommands() {
        assertEquals(
                "hechaoeconomy:pay Player_51 12.50 confirm",
                PlayerPaymentInput.command("Player_51", "12.50"));
        assertNull(PlayerPaymentInput.command("Player 51", "12.50"));
        assertNull(PlayerPaymentInput.command("Player_51", "0"));
    }
}

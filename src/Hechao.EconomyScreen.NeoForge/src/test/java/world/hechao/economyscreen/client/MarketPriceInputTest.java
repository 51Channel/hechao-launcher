package world.hechao.economyscreen.client;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.math.BigDecimal;
import org.junit.jupiter.api.Test;

final class MarketPriceInputTest {
    @Test
    void acceptsMoneyWithAtMostTwoDecimalPlaces() {
        assertTrue(MarketPriceInput.accepts(""));
        assertTrue(MarketPriceInput.accepts("19"));
        assertTrue(MarketPriceInput.accepts("19.95"));
        assertFalse(MarketPriceInput.accepts("19.999"));
        assertFalse(MarketPriceInput.accepts("-1"));
        assertFalse(MarketPriceInput.accepts("abc"));
    }

    @Test
    void enforcesTheMinimumTotalPrice() {
        assertEquals(new BigDecimal("1.00"), MarketPriceInput.parse("1.00"));
        assertNull(MarketPriceInput.parse("0.99"));
        assertNull(MarketPriceInput.parse(""));
    }
}

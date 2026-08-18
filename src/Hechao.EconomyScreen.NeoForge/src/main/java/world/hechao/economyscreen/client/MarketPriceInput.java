package world.hechao.economyscreen.client;

import java.math.BigDecimal;

final class MarketPriceInput {
    private MarketPriceInput() {
    }

    static boolean accepts(String value) {
        return value != null && value.matches("\\d{0,10}(?:\\.\\d{0,2})?");
    }

    static BigDecimal parse(String value) {
        if (value == null || value.isBlank() || !accepts(value)) {
            return null;
        }
        try {
            var amount = new BigDecimal(value);
            return amount.compareTo(BigDecimal.ONE) >= 0 ? amount : null;
        } catch (NumberFormatException ignored) {
            return null;
        }
    }
}

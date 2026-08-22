package world.hechao.economyscreen.client;

import java.math.BigDecimal;
import java.util.regex.Pattern;

final class PlayerPaymentInput {
    private static final Pattern AMOUNT = Pattern.compile(
            "\\d{0,10}(?:\\.\\d{0,2})?");

    private PlayerPaymentInput() {
    }

    static boolean acceptsAmount(String value) {
        return value != null && AMOUNT.matcher(value).matches();
    }

    static BigDecimal parseAmount(String value) {
        if (value == null || value.isBlank() || !acceptsAmount(value)) {
            return null;
        }
        try {
            var amount = new BigDecimal(value);
            return amount.signum() > 0 ? amount : null;
        } catch (NumberFormatException ignored) {
            return null;
        }
    }

    static String command(String playerName, String amountText) {
        var amount = parseAmount(amountText);
        if (!TeamCommandInput.validPlayerName(playerName) || amount == null) {
            return null;
        }
        return "hechaoeconomy:pay " + playerName + " "
                + amount.toPlainString() + " confirm";
    }
}

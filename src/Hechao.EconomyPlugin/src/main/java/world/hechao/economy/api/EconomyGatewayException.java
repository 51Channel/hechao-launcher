package world.hechao.economy.api;

public final class EconomyGatewayException extends Exception {
    private final boolean outcomeUnknown;
    private final int statusCode;

    public EconomyGatewayException(String message, boolean outcomeUnknown, int statusCode) {
        super(message);
        this.outcomeUnknown = outcomeUnknown;
        this.statusCode = statusCode;
    }

    public EconomyGatewayException(String message, Throwable cause, boolean outcomeUnknown) {
        super(message, cause);
        this.outcomeUnknown = outcomeUnknown;
        this.statusCode = 0;
    }

    public boolean isOutcomeUnknown() {
        return outcomeUnknown;
    }

    public int statusCode() {
        return statusCode;
    }
}

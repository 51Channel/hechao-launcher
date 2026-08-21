package world.hechao.economy.api;

public final class EconomyGatewayException extends Exception {
    private final boolean outcomeUnknown;
    private final int statusCode;
    private final String errorCode;

    public EconomyGatewayException(String message, boolean outcomeUnknown, int statusCode) {
        this(message, outcomeUnknown, statusCode, null);
    }

    public EconomyGatewayException(
            String message,
            boolean outcomeUnknown,
            int statusCode,
            String errorCode) {
        super(message);
        this.outcomeUnknown = outcomeUnknown;
        this.statusCode = statusCode;
        this.errorCode = errorCode;
    }

    public EconomyGatewayException(String message, Throwable cause, boolean outcomeUnknown) {
        super(message, cause);
        this.outcomeUnknown = outcomeUnknown;
        this.statusCode = 0;
        this.errorCode = null;
    }

    public boolean isOutcomeUnknown() {
        return outcomeUnknown;
    }

    public int statusCode() {
        return statusCode;
    }

    public String errorCode() {
        return errorCode;
    }
}

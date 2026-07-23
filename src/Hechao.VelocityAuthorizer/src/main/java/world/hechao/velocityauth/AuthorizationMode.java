package world.hechao.velocityauth;

enum AuthorizationMode {
    DISABLED,
    MONITOR,
    ENFORCE;

    static AuthorizationMode parse(String value) {
        return switch (value.trim().toLowerCase()) {
            case "disabled" -> DISABLED;
            case "monitor" -> MONITOR;
            case "enforce" -> ENFORCE;
            default -> throw new IllegalArgumentException("mode must be disabled, monitor, or enforce");
        };
    }
}

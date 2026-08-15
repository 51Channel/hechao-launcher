package world.hechao.velocityauth;

import java.util.Locale;
import java.util.regex.Pattern;

record AuthorizationDecision(
        boolean allowed,
        String reason,
        String message,
        String serverId,
        String velocityTarget,
        String backendHost,
        Integer backendPort) {

    private static final Pattern TARGET_PATTERN =
            Pattern.compile("^[a-z0-9][a-z0-9._-]{0,63}$");

    AuthorizationDecision(
            boolean allowed,
            String reason,
            String message,
            String serverId,
            String velocityTarget) {
        this(allowed, reason, message, serverId, velocityTarget, null, null);
    }

    static AuthorizationDecision fromJson(String json) {
        FlatJsonObject object = FlatJsonObject.parse(json);
        return new AuthorizationDecision(
                object.requiredBoolean("allowed"),
                object.requiredString("reason"),
                object.requiredString("message"),
                object.nullableString("serverId"),
                object.requiredString("velocityTarget"),
                object.nullableString("backendHost"),
                object.nullableInteger("backendPort"));
    }

    boolean hasSessionServerId() {
        return serverId != null
                && TARGET_PATTERN.matcher(serverId.toLowerCase(Locale.ROOT)).matches();
    }

    boolean hasVelocityTarget() {
        return velocityTarget != null
                && TARGET_PATTERN.matcher(velocityTarget.toLowerCase(Locale.ROOT)).matches();
    }

    boolean hasDynamicBackend() {
        return backendHost != null && backendPort != null;
    }

    boolean hasValidDynamicBackend() {
        if (backendHost == null && backendPort == null) {
            return true;
        }
        return ("127.0.0.1".equals(backendHost) || "::1".equals(backendHost))
                && backendPort != null
                && backendPort >= 1
                && backendPort <= 65535;
    }

    boolean requiresImmediateDenial() {
        return "MinecraftVersionMismatch".equals(reason)
                || "ClientProfileMismatch".equals(reason);
    }
}

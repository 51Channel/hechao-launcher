package world.hechao.velocityauth;

import java.util.Locale;
import java.util.regex.Pattern;

record AuthorizationDecision(
        boolean allowed,
        String reason,
        String message,
        String serverId,
        String velocityTarget) {

    private static final Pattern TARGET_PATTERN =
            Pattern.compile("^[a-z0-9][a-z0-9._-]{0,63}$");

    static AuthorizationDecision fromJson(String json) {
        FlatJsonObject object = FlatJsonObject.parse(json);
        return new AuthorizationDecision(
                object.requiredBoolean("allowed"),
                object.requiredString("reason"),
                object.requiredString("message"),
                object.nullableString("serverId"),
                object.requiredString("velocityTarget"));
    }

    boolean hasSessionServerId() {
        return serverId != null
                && TARGET_PATTERN.matcher(serverId.toLowerCase(Locale.ROOT)).matches();
    }

    boolean hasVelocityTarget() {
        return velocityTarget != null
                && TARGET_PATTERN.matcher(velocityTarget.toLowerCase(Locale.ROOT)).matches();
    }

    boolean requiresImmediateDenial() {
        return "MinecraftVersionMismatch".equals(reason)
                || "ClientProfileMismatch".equals(reason);
    }
}

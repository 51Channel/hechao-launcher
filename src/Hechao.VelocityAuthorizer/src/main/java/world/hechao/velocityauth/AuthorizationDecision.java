package world.hechao.velocityauth;

record AuthorizationDecision(
        boolean allowed,
        String reason,
        String message,
        String serverId,
        String velocityTarget) {

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
        return serverId != null && !serverId.isBlank();
    }

    boolean requiresImmediateDenial() {
        return "MinecraftVersionMismatch".equals(reason)
                || "ClientProfileMismatch".equals(reason);
    }
}

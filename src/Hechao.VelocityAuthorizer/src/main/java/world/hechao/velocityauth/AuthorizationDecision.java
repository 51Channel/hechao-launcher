package world.hechao.velocityauth;

record AuthorizationDecision(
        boolean allowed,
        String reason,
        String message,
        String serverId) {

    static AuthorizationDecision fromJson(String json) {
        FlatJsonObject object = FlatJsonObject.parse(json);
        return new AuthorizationDecision(
                object.requiredBoolean("allowed"),
                object.requiredString("reason"),
                object.requiredString("message"),
                object.nullableString("serverId"));
    }
}

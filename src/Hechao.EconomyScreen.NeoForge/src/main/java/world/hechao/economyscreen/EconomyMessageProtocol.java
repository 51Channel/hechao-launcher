package world.hechao.economyscreen;

import java.util.Optional;
import java.util.UUID;

public final class EconomyMessageProtocol {
    public static final String PREFIX = "[赫朝经济]";

    private static final String AUTHORIZATION_PREFIX = PREFIX + " 菜单授权已通过: ";
    private static final String REJECTION_PREFIX = PREFIX + " 菜单授权已拒绝: ";

    private EconomyMessageProtocol() {
    }

    public static String authorization(UUID sessionId, String actionId) {
        return AUTHORIZATION_PREFIX + scope(sessionId, actionId);
    }

    public static boolean isAuthorization(
            String message,
            UUID sessionId,
            String actionId) {
        return message != null
                && message.equals(authorization(sessionId, actionId));
    }

    public static boolean isAuthorizationReceipt(String message) {
        return message != null && message.startsWith(AUTHORIZATION_PREFIX);
    }

    public static String rejection(
            UUID sessionId,
            String actionId,
            String reason) {
        if (reason == null || reason.isBlank()
                || reason.contains("\n") || reason.contains("\r")) {
            throw new IllegalArgumentException("menu rejection reason is invalid");
        }
        return REJECTION_PREFIX + scope(sessionId, actionId) + ":" + reason;
    }

    public static Optional<String> rejectionReason(
            String message,
            UUID sessionId,
            String actionId) {
        String prefix = REJECTION_PREFIX + scope(sessionId, actionId) + ":";
        if (message == null || !message.startsWith(prefix)) {
            return Optional.empty();
        }
        String reason = message.substring(prefix.length());
        return reason.isBlank() ? Optional.empty() : Optional.of(reason);
    }

    public static boolean isMenuSessionReceipt(String message) {
        return isAuthorizationReceipt(message)
                || (message != null && message.startsWith(REJECTION_PREFIX));
    }

    private static String scope(UUID sessionId, String actionId) {
        if (sessionId == null) {
            throw new IllegalArgumentException("menu session id is invalid");
        }
        if (actionId == null || !actionId.matches("[a-z0-9_]{1,32}")) {
            throw new IllegalArgumentException("menu action id is invalid");
        }
        return sessionId + ":" + actionId;
    }
}

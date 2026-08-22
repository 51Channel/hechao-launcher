package world.hechao.economyscreen.client;

import java.util.List;

final class PlayerTeleportReceipt {
    private static final List<String> REQUEST_RESPONSES = List.of(
            "找不到在线玩家",
            "不能向自己发送传送请求",
            "对方当前不接收 TPA 请求",
            "请求已自动接受，5 秒后执行传送",
            "传送请求已发送，60 秒内等待回应",
            "无法读取对方设置",
            "用法：/");
    private static final List<String> ACCEPT_RESPONSES = List.of(
            "没有可接受的传送请求",
            "请求者已离线，传送请求已取消",
            "已接受请求，5 秒后执行传送");
    private static final List<String> DENY_RESPONSES = List.of(
            "没有可拒绝的传送请求",
            "已拒绝传送请求");
    private static final List<String> REQUEST_ERRORS = List.of(
            "找不到在线玩家",
            "不能向自己发送传送请求",
            "对方当前不接收 TPA 请求",
            "无法读取对方设置",
            "用法：/");
    private static final List<String> ACCEPT_ERRORS = List.of(
            "没有可接受的传送请求",
            "请求者已离线，传送请求已取消");
    private static final List<String> DENY_ERRORS = List.of(
            "没有可拒绝的传送请求");

    private PlayerTeleportReceipt() {
    }

    static boolean matches(Operation operation, String message) {
        if (operation == null || message == null) {
            return false;
        }
        var responses = switch (operation) {
            case SEND_TO_PLAYER, INVITE_PLAYER -> REQUEST_RESPONSES;
            case ACCEPT -> ACCEPT_RESPONSES;
            case DENY -> DENY_RESPONSES;
        };
        return responses.stream().anyMatch(message::contains);
    }

    static boolean isError(Operation operation, String message) {
        if (operation == null || message == null) {
            return true;
        }
        var errors = switch (operation) {
            case SEND_TO_PLAYER, INVITE_PLAYER -> REQUEST_ERRORS;
            case ACCEPT -> ACCEPT_ERRORS;
            case DENY -> DENY_ERRORS;
        };
        return errors.stream().anyMatch(message::contains);
    }

    enum Operation {
        SEND_TO_PLAYER,
        INVITE_PLAYER,
        ACCEPT,
        DENY
    }
}

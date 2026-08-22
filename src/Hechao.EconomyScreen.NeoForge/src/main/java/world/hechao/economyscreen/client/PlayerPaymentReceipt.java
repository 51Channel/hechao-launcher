package world.hechao.economyscreen.client;

import java.util.List;
import java.util.Locale;
import world.hechao.economyscreen.EconomyMessageProtocol;

final class PlayerPaymentReceipt {
    private static final List<String> RESPONSE_MARKERS = List.of(
            "已向 ",
            "转账失败",
            "接收者必须",
            "金额必须",
            "用法: /pay",
            "大额转账",
            "经济服务当前不可交易",
            "经济服务暂时无法确认请求结果",
            "经济请求被拒绝（HTTP");
    private static final List<String> ERROR_MARKERS = List.of(
            "失败",
            "错误",
            "必须",
            "不可",
            "拒绝",
            "断开",
            "超时",
            "失效",
            "太快",
            "找不到",
            "无法",
            "请再次",
            "http");

    private PlayerPaymentReceipt() {
    }

    static boolean matches(String message) {
        return message != null
                && message.contains(EconomyMessageProtocol.PREFIX)
                && RESPONSE_MARKERS.stream().anyMatch(message::contains);
    }

    static boolean isError(String message) {
        if (message == null) {
            return true;
        }
        String lower = message.toLowerCase(Locale.ROOT);
        return ERROR_MARKERS.stream().anyMatch(lower::contains);
    }
}

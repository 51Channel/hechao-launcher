package world.hechao.economyscreen.client;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

final class EconomyResultState {
    private static final int TIMEOUT_TICKS = 200;
    private static final int MAX_MESSAGES = 6;
    private static final List<String> ERROR_MARKERS = List.of(
            "失败",
            "错误",
            "不可",
            "拒绝",
            "没有权限",
            "没有可出售",
            "过期",
            "不存在",
            "无法",
            "取消",
            "失效",
            "太快",
            "联系管理员",
            "http");
    private static final List<String> TEAM_RESPONSE_MARKERS = List.of(
            "队伍",
            "队长",
            "成员");

    private final String actionId;
    private final ArrayList<String> messages = new ArrayList<>();
    private String loadingMessage;
    private Tone tone = Tone.LOADING;
    private int waitingTicks;

    EconomyResultState(String actionId, String loadingMessage) {
        this.actionId = actionId;
        this.loadingMessage = loadingMessage;
    }

    void accept(String rawMessage) {
        String message = normalize(rawMessage);
        if (message.isBlank()) {
            return;
        }
        if (messages.size() == MAX_MESSAGES) {
            messages.removeFirst();
        }
        messages.add(message);
        tone = isError(message) ? Tone.ERROR : Tone.SUCCESS;
        waitingTicks = 0;
    }

    void begin(String message) {
        messages.clear();
        loadingMessage = message;
        tone = Tone.LOADING;
        waitingTicks = 0;
    }

    void tick() {
        if (tone != Tone.LOADING || ++waitingTicks < TIMEOUT_TICKS) {
            return;
        }
        messages.add("请求超时，请稍后重试。");
        tone = Tone.ERROR;
    }

    List<String> messages() {
        return List.copyOf(messages);
    }

    String loadingMessage() {
        return loadingMessage;
    }

    Tone tone() {
        return tone;
    }

    boolean isAction(String expectedActionId) {
        return actionId.equals(expectedActionId);
    }

    boolean acceptsUnprefixedMessage(String message) {
        return isAction("team")
                && TEAM_RESPONSE_MARKERS.stream().anyMatch(message::contains);
    }

    boolean canConfirmSale() {
        return "sell".equals(actionId)
                && tone == Tone.SUCCESS
                && messages.stream().anyMatch(message -> message.startsWith("报价:"));
    }

    static String normalize(String rawMessage) {
        int prefix = rawMessage.indexOf(ClientEconomyUiBridge.ECONOMY_PREFIX);
        String normalized = prefix >= 0
                ? rawMessage.substring(prefix + ClientEconomyUiBridge.ECONOMY_PREFIX.length())
                : rawMessage;
        return normalized.strip();
    }

    private static boolean isError(String message) {
        String lower = message.toLowerCase(Locale.ROOT);
        return ERROR_MARKERS.stream().anyMatch(lower::contains);
    }

    enum Tone {
        LOADING,
        SUCCESS,
        ERROR
    }
}

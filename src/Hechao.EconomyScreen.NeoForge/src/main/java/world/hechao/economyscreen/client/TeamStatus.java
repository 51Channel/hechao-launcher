package world.hechao.economyscreen.client;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

final class TeamStatus {
    private static final int TIMEOUT_TICKS = 200;
    private static final List<String> RESPONSE_MARKERS = List.of(
            "队伍",
            "队长",
            "成员",
            "队员",
            "队伍信息",
            "队伍人数",
            "邀请",
            "加入队伍",
            "离开队伍",
            "解散",
            "移出",
            "踢出",
            "找不到在线玩家",
            "不能邀请自己",
            "待处理");
    private static final List<String> ERROR_MARKERS = List.of(
            "失败",
            "无法",
            "不能",
            "不是",
            "没有",
            "未找到",
            "过期",
            "断开",
            "失效",
            "太快",
            "不可用");

    private Membership membership = Membership.LOADING;
    private String leader = "";
    private List<String> members = List.of();
    private String feedback = "正在同步队伍状态...";
    private boolean waiting = true;
    private boolean error;
    private int waitingTicks;

    void begin(String message) {
        feedback = message;
        waiting = true;
        error = false;
        waitingTicks = 0;
    }

    void note(String message) {
        feedback = message;
        waiting = false;
        error = false;
        waitingTicks = 0;
    }

    void accept(String rawMessage) {
        String message = EconomyResultState.normalize(rawMessage);
        if (message.isBlank()) {
            return;
        }
        waiting = false;
        waitingTicks = 0;
        error = isError(message);
        feedback = message;

        if (withoutTeam(message)) {
            membership = Membership.NONE;
            leader = "";
            members = List.of();
            return;
        }

        String parsedLeader = labelValue(message, "队长");
        if (!parsedLeader.isBlank()) {
            membership = Membership.JOINED;
            leader = parsedLeader;
            mergeMember(parsedLeader);
        }

        String teamMembers = firstLabelValue(message, "队伍成员", "队员", "成员");
        if (!teamMembers.isBlank()) {
            parseMembers(teamMembers);
            return;
        }

        String parsedMembers = firstLabelValue(message, "队员", "成员");
        if (!parsedMembers.isBlank()) {
            membership = Membership.JOINED;
            parseMembers(parsedMembers);
        }
    }

    void tick() {
        if (!waiting || ++waitingTicks < TIMEOUT_TICKS) {
            return;
        }
        waiting = false;
        error = true;
        feedback = "请求超时，请重新刷新。";
    }

    boolean accepts(String message) {
        return RESPONSE_MARKERS.stream().anyMatch(message::contains);
    }

    Membership membership() {
        return membership;
    }

    String leader() {
        return leader;
    }

    List<String> members() {
        return members;
    }

    String feedback() {
        return feedback;
    }

    boolean waiting() {
        return waiting;
    }

    boolean error() {
        return error;
    }

    private void parseMembers(String value) {
        var parsed = new ArrayList<String>();
        String parsedLeader = leader;
        for (String raw : value.split("[,，、|]")) {
            String member = raw.strip();
            boolean owner = member.contains("队长");
            member = member.replaceFirst("^[\\[【]队长[\\]】]\\s*", "").strip();
            if (member.startsWith("队长:") || member.startsWith("队长：")) {
                owner = true;
                member = member.substring(3).strip();
            }
            member = member.replaceFirst("^(队员|成员)[:：]\\s*", "").strip();
            if (!member.isBlank() && !parsed.contains(member)) {
                parsed.add(member);
                if (owner) {
                    parsedLeader = member;
                }
            }
        }
        if (!parsedLeader.isBlank() && !parsed.contains(parsedLeader)) {
            parsed.addFirst(parsedLeader);
        }
        membership = Membership.JOINED;
        leader = parsedLeader;
        members = List.copyOf(parsed);
    }

    private void mergeMember(String playerName) {
        if (members.contains(playerName)) {
            return;
        }
        var merged = new ArrayList<>(members);
        merged.addFirst(playerName);
        members = List.copyOf(merged);
    }

    private static String firstLabelValue(String message, String... labels) {
        for (String label : labels) {
            String value = labelValue(message, label);
            if (!value.isBlank()) {
                return value;
            }
        }
        return "";
    }

    private static String labelValue(String message, String label) {
        int labelAt = message.indexOf(label);
        if (labelAt < 0) {
            return "";
        }
        int colon = Math.max(
                message.indexOf(':', labelAt + label.length()),
                message.indexOf('：', labelAt + label.length()));
        if (colon < 0) {
            return "";
        }
        String value = message.substring(colon + 1).strip();
        for (String nextLabel : List.of("队伍成员", "队员", "成员", "队长")) {
            int next = value.indexOf(nextLabel);
            if (next > 0 && isLabelBoundary(value, next + nextLabel.length())) {
                value = value.substring(0, next).strip();
            }
        }
        return value.replaceFirst("[,，、|。.!！]$", "").strip();
    }

    private static boolean isLabelBoundary(String value, int afterLabel) {
        return afterLabel < value.length()
                && (value.charAt(afterLabel) == ':'
                        || value.charAt(afterLabel) == '：');
    }

    private static boolean withoutTeam(String message) {
        return message.contains("不在队伍")
                || message.contains("没有队伍")
                || message.contains("未加入队伍")
                || message.contains("尚未加入队伍")
                || message.contains("你没有队伍");
    }

    private static boolean isError(String message) {
        String lower = message.toLowerCase(Locale.ROOT);
        return ERROR_MARKERS.stream().anyMatch(lower::contains);
    }

    enum Membership {
        LOADING,
        NONE,
        JOINED
    }
}

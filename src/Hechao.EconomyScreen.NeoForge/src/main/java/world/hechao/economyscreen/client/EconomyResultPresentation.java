package world.hechao.economyscreen.client;

import java.util.List;
import java.util.Optional;
import java.util.regex.Pattern;

final class EconomyResultPresentation {
    private static final Pattern BALANCE = Pattern.compile(
            "^(.+?)\\s+的余额:\\s*([+-]?\\d+(?:\\.\\d+)?)\\s*金币[。.]?$");
    private static final Pattern QUOTE = Pattern.compile(
            "^报价:\\s*(\\d+)\\s*个\\s+(.+?)\\s*=\\s*"
                    + "([+-]?\\d+(?:\\.\\d+)?)\\s*金币[。.]?$");
    private static final Pattern SALE_SUCCESS = Pattern.compile(
            "^出售成功，获得\\s*([+-]?\\d+(?:\\.\\d+)?)\\s*金币[。.]?$");

    private EconomyResultPresentation() {
    }

    static View from(EconomyResultState state) {
        if (state.tone() == EconomyResultState.Tone.LOADING) {
            return new View(
                    Kind.LOADING,
                    "经济终端",
                    state.loadingMessage(),
                    "",
                    "正在等待服务器响应",
                    "",
                    "");
        }

        List<String> messages = state.messages();
        if (state.tone() == EconomyResultState.Tone.ERROR) {
            return new View(
                    Kind.ERROR,
                    "请求状态",
                    "操作未完成",
                    "",
                    "请检查提示后重试",
                    String.join("  ", messages),
                    "");
        }

        for (String message : messages) {
            var balance = balance(message);
            if (balance.isPresent()) {
                var value = balance.get();
                return new View(
                        Kind.BALANCE,
                        "可用余额",
                        value.amount(),
                        "金币",
                        value.playerName() + " · 查询完成",
                        "",
                        "");
            }
        }

        for (String message : messages) {
            var match = QUOTE.matcher(message);
            if (match.matches()) {
                String instruction = messages.stream()
                        .filter(value -> value.contains("秒内") && value.contains("confirm"))
                        .findFirst()
                        .orElse("");
                return new View(
                        Kind.QUOTE,
                        "预计回收金额",
                        match.group(3),
                        "金币",
                        match.group(1) + " 个 · " + match.group(2),
                        instruction,
                        match.group(2));
            }
        }

        for (String message : messages) {
            var match = SALE_SUCCESS.matcher(message);
            if (match.matches()) {
                return new View(
                        Kind.SALE_SUCCESS,
                        "本次入账",
                        match.group(1),
                        "金币",
                        "交易已完成",
                        message,
                        "");
            }
        }

        return new View(
                Kind.SUCCESS,
                "请求状态",
                "操作完成",
                "",
                "服务器已确认",
                String.join("  ", messages),
                "");
    }

    static Optional<Balance> balance(String message) {
        var match = BALANCE.matcher(EconomyResultState.normalize(message));
        return match.matches()
                ? Optional.of(new Balance(match.group(1), match.group(2)))
                : Optional.empty();
    }

    enum Kind {
        LOADING,
        BALANCE,
        QUOTE,
        SALE_SUCCESS,
        SUCCESS,
        ERROR
    }

    record View(
            Kind kind,
            String label,
            String primary,
            String unit,
            String secondary,
            String detail,
            String itemId) {
        boolean hasMonetaryValue() {
            return kind == Kind.BALANCE
                    || kind == Kind.QUOTE
                    || kind == Kind.SALE_SUCCESS;
        }
    }

    record Balance(String playerName, String amount) {
    }
}

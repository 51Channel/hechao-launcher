package world.hechao.economyscreen;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Optional;

public final class MenuActions {
    private static final Map<String, Definition> ACTIONS = createActions();

    private MenuActions() {
    }

    static Map<String, Definition> all() {
        return ACTIONS;
    }

    public static Optional<Definition> find(String actionId) {
        return Optional.ofNullable(ACTIONS.get(actionId));
    }

    private static Map<String, Definition> createActions() {
        var actions = new LinkedHashMap<String, Definition>();
        actions.put(
                "balance",
                new Definition(
                        "个人账户",
                        "查看余额并管理个人交易",
                        "正在同步账户...",
                        "hechaoeconomy:money"));
        actions.put(
                "shop",
                new Definition(
                        "回收目录",
                        "查看当前可出售物品",
                        "正在打开回收目录...",
                        "hechaoeconomy:shop"));
        actions.put(
                "sell",
                new Definition(
                        "上架物品",
                        "放入物品并设置玩家市场售价",
                        "正在打开市场上架...",
                        "hechaoeconomy:ah sell"));
        actions.put(
                "market",
                new Definition(
                        "玩家市场",
                        "浏览、上架和领取玩家交易物品",
                        "正在打开玩家市场...",
                        "hechaoeconomy:ah"));
        actions.put(
                "settings",
                new Definition(
                        "个人设置",
                        "打开生存服个人设置",
                        "正在打开个人设置...",
                        "skyrealmcore:settings"));
        actions.put(
                "team",
                new Definition(
                        "我的队伍",
                        "管理邀请、成员和队伍聊天",
                        "正在打开队伍...",
                        "skyrealmcore:team list"));
        return Collections.unmodifiableMap(actions);
    }

    public record Definition(
            String label,
            String description,
            String feedback,
            String command) {
    }
}

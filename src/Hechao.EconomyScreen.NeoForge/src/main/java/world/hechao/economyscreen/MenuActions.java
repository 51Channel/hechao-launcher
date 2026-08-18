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
                        "我的余额",
                        "查看当前金币余额",
                        "正在查询余额...",
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
                        "出售物品",
                        "放入物品并确认回收",
                        "正在打开出售界面...",
                        "hechaoeconomy:sell"));
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
                        "打开队伍功能",
                        "正在打开队伍...",
                        "skyrealmcore:team"));
        return Collections.unmodifiableMap(actions);
    }

    public record Definition(
            String label,
            String description,
            String feedback,
            String command) {
    }
}

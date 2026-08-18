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
                        "hechaoeconomy:money",
                        false));
        actions.put(
                "shop",
                new Definition(
                        "回收目录",
                        "查看当前可出售物品",
                        "正在打开回收目录...",
                        "hechaoeconomy:shop",
                        false));
        actions.put(
                "sell",
                new Definition(
                        "出售主手",
                        "为主手物品创建报价",
                        "正在处理主手物品...",
                        "hechaoeconomy:sell",
                        false));
        actions.put(
                "admin_product",
                new Definition(
                        "服主回收设置",
                        "手持物品配置回收价（管理员）",
                        "正在打开回收设置...",
                        "hechaoeconomy:heco product",
                        true));
        actions.put(
                "settings",
                new Definition(
                        "个人设置",
                        "打开生存服个人设置",
                        "正在打开个人设置...",
                        "settings",
                        false));
        actions.put(
                "team",
                new Definition(
                        "我的队伍",
                        "打开队伍功能",
                        "正在打开队伍...",
                        "team",
                        false));
        return Collections.unmodifiableMap(actions);
    }

    public record Definition(
            String label,
            String description,
            String feedback,
            String command,
            boolean administratorOnly) {
    }
}

package world.hechao.economyscreen;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

final class MenuActions {
    private MenuActions() {
    }

    static Map<String, Definition> all() {
        var actions = new LinkedHashMap<String, Definition>();
        actions.put("balance", new Definition("我的余额", "查看当前金币余额", "money"));
        actions.put("shop", new Definition("回收目录", "查看当前可出售物品", "shop"));
        actions.put("sell", new Definition("出售主手", "为主手物品创建报价", "sell"));
        actions.put(
                "admin_product",
                new Definition("服主回收设置", "手持物品配置回收价（管理员）", "heco product"));
        actions.put("settings", new Definition("个人设置", "打开生存服个人设置", "settings"));
        actions.put("team", new Definition("我的队伍", "打开队伍功能", "team"));
        return Collections.unmodifiableMap(actions);
    }

    record Definition(String label, String description, String command) {
    }
}

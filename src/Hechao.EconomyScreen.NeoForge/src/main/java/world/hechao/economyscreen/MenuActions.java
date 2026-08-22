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
                        "hechaoeconomy:money",
                        ExecutionMode.SERVER));
        actions.put(
                "shop",
                new Definition(
                        "回收目录",
                        "查看当前可出售物品",
                        "正在打开回收目录...",
                        "hechaoeconomy:shop",
                        ExecutionMode.SERVER));
        actions.put(
                "market",
                new Definition(
                        "玩家市场",
                        "浏览、上架和领取玩家交易物品",
                        "正在打开玩家市场...",
                        "hechaoeconomy:ah",
                        ExecutionMode.SERVER));
        actions.put(
                "sell",
                new Definition(
                        "市场上架",
                        "玩家市场快捷入口：放入物品并设置售价",
                        "正在打开市场上架...",
                        "hechaoeconomy:ah sell",
                        ExecutionMode.SERVER));
        actions.put(
                "market_mine",
                new Definition(
                        "我的挂单",
                        "查看并管理自己正在出售的物品",
                        "正在打开我的挂单...",
                        "hechaoeconomy:ah mine",
                        ExecutionMode.SERVER));
        actions.put(
                "market_claim",
                new Definition(
                        "待领取",
                        "领取玩家市场购买完成的物品",
                        "正在打开待领取物品...",
                        "hechaoeconomy:ah claim",
                        ExecutionMode.SERVER));
        actions.put(
                "payment",
                new Definition(
                        "玩家转账",
                        "向在线玩家安全转账",
                        "正在打开转账终端...",
                        "hechaoeconomy:pay",
                        ExecutionMode.CLIENT_SCREEN));
        actions.put(
                "team",
                new Definition(
                        "我的队伍",
                        "管理邀请、成员和队伍聊天",
                        "正在打开队伍...",
                        "skyrealmcore:team list",
                        ExecutionMode.SERVER));
        actions.put(
                "teleport",
                new Definition(
                        "玩家传送",
                        "发起、接受或拒绝玩家传送请求",
                        "正在打开传送终端...",
                        "skyrealmcore:tpa",
                        ExecutionMode.CLIENT_SCREEN));
        actions.put(
                "home",
                new Definition(
                        "返回家园",
                        "传送到当前默认家园",
                        "正在返回家园...",
                        "essentials:home",
                        ExecutionMode.SERVER));
        actions.put(
                "spawn",
                new Definition(
                        "返回主城",
                        "传送到服务器主城出生点",
                        "正在返回主城...",
                        "essentialsspawn:spawn",
                        ExecutionMode.SERVER));
        actions.put(
                "back",
                new Definition(
                        "返回上次位置",
                        "返回最近一次传送或死亡前的位置",
                        "正在返回上次位置...",
                        "essentials:back",
                        ExecutionMode.SERVER));
        actions.put(
                "claims",
                new Definition(
                        "我的领地",
                        "查看领地与可用领地方块",
                        "正在查询领地...",
                        "griefprevention:claimslist",
                        ExecutionMode.SERVER));
        actions.put(
                "settings",
                new Definition(
                        "个人设置",
                        "打开生存服个人设置",
                        "正在打开个人设置...",
                        "skyrealmcore:settings",
                        ExecutionMode.SERVER));
        return Collections.unmodifiableMap(actions);
    }

    public record Definition(
            String label,
            String description,
            String feedback,
            String command,
            ExecutionMode executionMode) {
    }

    public enum ExecutionMode {
        SERVER,
        CLIENT_SCREEN
    }
}

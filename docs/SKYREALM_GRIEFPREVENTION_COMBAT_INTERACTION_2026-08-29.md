# 天域远征 GriefPrevention 战斗交互限制关闭

- 变更日期：`2026-08-29`
- 目标：owl5 `activity-survival`
- 组件：GriefPrevention `16.18.5`
- 变更方式：配置热重载，不重启 Minecraft

## 玩家问题

玩家互相造成一次伤害后，约 15 秒内无法打开箱子、使用工作站或完成部分交互。反馈表现为
“摸了别人一下，半天不能互动”。

## 根因

生产配置在主世界、下界和末地都启用了 GriefPrevention PvP 规则，并设置：

```yaml
GriefPrevention:
  PvP:
    CombatTimeoutSeconds: 15
```

对 GriefPrevention `16.18.5` 对应源码和生产 JAR 反编译结果的核对确认：

- 玩家受伤和攻击时都会更新 `lastPvpTimestamp`；
- `PlayerData.inPvpCombat()` 使用 `elapsed > CombatTimeoutSeconds * 1000` 判断战斗状态结束；
- 方块容器、工作站和存储矿车交互都会在 `inPvpCombat()` 为真时拒绝；
- 同一状态还用于限制原配置中的传送指令、丢物、领地内建造、创建领地和战斗退出。

因此该问题不是 Tom's Simple Storage、经济屏幕或 Essentials 导致，而是 GriefPrevention 的
战斗标记策略。

## 生产变更

将唯一配置项原子修改为：

```yaml
CombatTimeoutSeconds: 0
```

设为 `0` 后，下一次状态检查只要距离伤害时间已经过去至少 1ms，战斗状态就会清除，玩家
实际操作中不再存在战斗交互等待。此变更关闭整套 GriefPrevention 战斗标记限制，包括：

- 战斗期间禁止打开箱子、工作站和存储实体；
- 战斗期间禁止原配置中的 `/home`、`/vanish`、`/spawn`、`/tpa`；
- 战斗期间禁止丢物、领地内建造和创建领地；
- 战斗退出惩罚。

以下能力没有关闭：

- 玩家之间的正常 PVP 伤害；
- 私有领地、管理员领地和子领地的 PVP 安全区；
- 领地内箱子、工作站、门、按钮和生物的授权检查；
- 防盗、领地边界、死亡掉落保护和其他 GriefPrevention 规则。

## 备份与热重载

修改前配置 SHA-256：
`FDE3FBA294532507B9C7637ED74A936E729874A3B4335AE1AAD87B862C3BEC2A`。

独立回滚备份：

`E:\manual-backups\activity-survival-griefprevention-combat-timeout-20260829T140541Z\config.yml`

脚本只允许恰好一个 `CombatTimeoutSeconds: 15` 匹配，并验证把新值反向替换为 `15` 后与
原始文本逐字符一致，从而保证没有修改 YAML 中其他字段。写入后执行控制台 `gpreload`，
插件明确返回：

```text
Configuration updated. If you have updated your Grief Prevention JAR,
you still need to /reload or reboot your server.
```

热重载结果：

- 配置值：`CombatTimeoutSeconds: 0`；
- 新配置 SHA-256：
  `8771F916AD2F9FB33A3FDAFB0D3EAFDFBA9364AC2D639F56E1DF861F9E1D8092`；
- Java PID 仍为 `8348`，计划任务仍为 `Running`；
- `127.0.0.1:25600` 仍为单监听；
- 修改前后均有 2 条玩家连接，未重启、未断开玩家；
- 自动回滚未触发。

在线玩家已收到测试通知。仍需真人完成一次“互相造成伤害后立即打开箱子/工作站”的最终
交互验收；领地内无授权玩家仍应继续被领地规则拒绝。

结构化证据见
[`evidence/SKYREALM_GRIEFPREVENTION_COMBAT_INTERACTION_2026-08-29.json`](evidence/SKYREALM_GRIEFPREVENTION_COMBAT_INTERACTION_2026-08-29.json)。

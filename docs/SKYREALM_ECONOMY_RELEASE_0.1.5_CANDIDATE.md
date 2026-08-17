# 天域远征工业季经济插件 0.1.5 兼容热修候选

- 候选日期：2026-08-17
- Bukkit 插件：`HechaoEconomy 0.1.5`
- NeoForge 双端模组：继续使用 `HechaoEconomyScreen 0.1.3`
- 屏幕协议：继续使用 `2`
- 生产状态：候选已验证，工业季当前保持停止

## 真实 Arclight 发现与修复

`0.1.3` 在工业季 Arclight 冷启动时调用较新 Paper API 的
`Server.getCommandMap()`，运行时发生 `NoSuchMethodError`。`0.1.4` 改用长期兼容的
`Server.getPluginCommand()` 后可以正常启动，但真实命令表显示 Essentials 在后加载并
覆盖 `money`、`balance`、`bal`、`pay`、`sell` 根命令，交易门禁因此正确关闭。

`0.1.5` 完成以下修复：

- 改为在 Essentials 之后加载，让赫朝经济插件最终注册并拥有经济根命令；
- 保留 Essentials `disabled-commands`，不恢复其本地经济入口；
- 同时核对 `money`、`balance`、`bal`、`pay`、`sell`、`shop`、`heco` 的插件归属；
- 缺失命令或其他插件拥有任一根命令时继续故障关闭；
- `/heco health` 分开显示 API 配置、Vault 权威、命令权威和最终可交易状态。

## 制品与验证

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoEconomy-0.1.5.jar` | 371,723 字节 | `304794280382DFC6002D316C41ABA7B07602A7C84F29BE443FC9D7A6271ABF47` |

- HechaoEconomy：`18/18`；
- 字节码只调用 `Server.getPluginCommand()`，不再引用 `Server.getCommandMap()`；
- 真实工业季冷启动确认 Essentials 先启用、HechaoEconomy `0.1.5` 后启用；
- PAPI expansion `hechao 0.1.5` 注册成功；
- `/heco health` 的 API、Vault、命令和可交易四项均为 `true`；
- 无 `NoSuchMethodError`、命令冲突或 Vault 所有权冲突；
- 候选验收后正常保存世界并停止，计划任务回到 `Ready`、`25600` 无监听。

最终生产替换须等待 API `0.33.1` 商品目录热修通过。服务端只替换 Bukkit JAR，不重发
客户端档案，不改 Screen `0.1.3`、令牌、配置、世界、启动脚本或 Velocity。

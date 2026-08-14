# LuckPerms Tier Agent 0.1.3 正式发布

- 正式版本：`HechaoLuckPermsTierAgent 0.1.3`
- 配套 API：`0.30.3-20260814T072942Z`
- 等级命令协议：`2`
- 制品源码提交：`e69cb9d93b71696d391999856fe7a2a86703161f`
- 正式标签：`luckperms-tier-agent-v0.1.3`
- 激活时间：2026-08-14 16:06 CST
- 激活范围：只重启 owl5 内部大厅

## 根因与隔离

`0.1.2` 的持久化主组和跨服缓存修复本身有效，但历史 PVP 返回验收目录
`E:\Lobby-PvpReturn-Staging` 仍由计划任务运行，并加载 `0.1.0`。该实例与正式大厅共用
`agent-id=owl5-lobby` 和内部同步凭据，会竞争领取真实等级命令。旧实例可以只修改计算
继承节点便回执 `Applied`，没有保证 MariaDB stored primary group，五分钟快照随后把后台
身份恢复为旧值。

只读核验确认该目录不在 Velocity 或服控路由中，`25580` 没有玩家连接。计划任务
`Hechao-Lobby-PvpReturn-Staging` 已禁用并停止，任务 XML 和状态备份保留；正式大厅、
Velocity、生存服和活动服未因隔离动作重启。最终复核时 `25580` 没有监听或已建立连接。

## 修复与制品

`0.1.3` 的领取和完成请求固定携带 `agentVersion=0.1.3` 与 `protocolVersion=2`。API
`0.30.3` 在访问命令仓库前要求精确协议 `2`，租约使用版本化代理身份，完成审计记录软件
版本和协议。旧代理即使仍持有同步凭据，也不能领取、续租或完成等级命令。

| 项目 | 结果 |
| --- | --- |
| JAR | `HechaoLuckPermsTierAgent-0.1.3.jar`，326,882 字节 |
| SHA-256 | `E0637E0AA0A549C5DBDD4FFB3E34238645E6AEA0FDB8BE3780C5822CDBC0700F` |
| Manifest / `plugin.yml` | `0.1.3` |
| Gradle | `16/16` |
| API | `311/311` |
| 完整 `.NET` 解决方案 | `731/731` |
| 可复现构建 | 连续两次 JAR SHA-256 一致 |
| 安装器 | 隔离成功路径和故意失败自动回滚路径均通过 |

## 生产部署

正式回滚点为：

`E:\manual-backups\luckperms-tier-agent-20260814T080510Z`

部署前等级命令队列为空，大厅 `25566` 没有已建立连接。第一次重启脚本在停服前因局部
变量与 PowerShell 只读 `$PID` 冲突而退出；第二次因英文 `list` 日志时序门禁未命中而自动
恢复 `0.1.2`。两次均没有影响其他 Java 进程。最终改用回环监听和已建立连接数作为零玩家
门禁后，只优雅重启内部大厅并成功加载 `0.1.3`。

大厅新 PID 为 `8028`。部署时其他 Java PID `2576 / 7748 / 9428` 的身份保持不变；日志
确认插件以 `owl5-lobby (version 0.1.3, protocol 2)` 启用并到达 `Done`。最终只读复核时
PID `2576` 已在本任务之外退出，Codex 没有为恢复旧 PID 集合启动或重启任何服务器。

## 协议与真实变更验收

- 精确旧版载荷返回 `400`，错误字段为 `agentVersion` 和 `protocolVersion`；
- 协议 `2` 的空领取返回 `200`，命令数为 `0`；
- API 和 Tier Agent 自部署起 warning/error 均为 `0`；
- 2026-08-14 08:42 UTC，管理员正常提交的两条真实 `vip` 变更均为 `Applied`。本次部署
  没有创建、重放或改写这些命令，也没有修改任何玩家身份；
- 08:43 UTC 快照变为 `default=100 / vip=14 / admin=0 / owner=3`，至 09:03 UTC 跨过
  四个五分钟间隔后仍保持该分布；
- 最终快照数为 `117`，快照与已绑定身份差异为 `0`，用户等级映射差异为 `0`，活动命令
  为 `0`；`Hechao Launcher LuckPerms Sync` 最近结果为 `0`；
- `25566` 只监听 `127.0.0.1` 且没有已建立连接，正式插件目录只存在 `0.1.3` JAR。

这组真实业务变化已经覆盖“正常变更跨两轮不回退”门槛。剩余外部验收仅是使用隔离测试
账号完成四级正向、改回、自身保护、最后管理员保护和拒绝路径，不影响本次回退修复结论。

## 回滚

若插件无法加载或同类回退复发，只优雅停止内部大厅，从上述回滚目录恢复 `0.1.2` JAR
和配置，再启动大厅。API 可独立回滚到 `0.30.2`，但会暂时失去服务端旧协议门禁。回滚
前必须确认没有 `Pending` / `Claimed` 命令；不得删除或重放真实玩家命令，也不得操作
Velocity、生存服或活动服。

结构化证据见
[`evidence/LUCKPERMS_TIER_AGENT_0.1.3_PRODUCTION_DEPLOYMENT_2026-08-14.json`](evidence/LUCKPERMS_TIER_AGENT_0.1.3_PRODUCTION_DEPLOYMENT_2026-08-14.json)。

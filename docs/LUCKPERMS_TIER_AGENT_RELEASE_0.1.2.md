# LuckPerms Tier Agent 0.1.2 正式发布

- 正式版本：`HechaoLuckPermsTierAgent 0.1.2`
- 目标生产 API：`0.30.2-20260811T124943Z`
- 数据库迁移：无
- 激活时间：2026-08-14 09:05 CST
- 激活范围：只重启 owl5 内部大厅

## 根因与修复

生产使用 `primary-group-calculation: parents-by-weight`。`0.1.1` 把
`User#getPrimaryGroup()` 的计算结果当成持久化 stored primary group；目标继承节点已
存在时会提前回执 `Applied`，没有修复 MariaDB stored value。保存后也没有调用
`MessagingService.pushUserUpdate`，其他服务器可能继续保留旧缓存。五分钟只读同步随后
从 MariaDB 读到旧值，后台身份因此恢复。

`0.1.2` 始终从存储加载用户，确保目标继承节点并清理其他受管节点，然后无条件调用
`setPrimaryGroup`、等待 `saveUser`，最后广播用户更新。消息服务不可用、节点变更失败、
保存或广播异常时均故障关闭；失败后重新加载用户，安装器也能恢复旧 JAR 和配置。

## 制品与自动验证

- JAR：`326,515` 字节；
- SHA-256：`917984C1DED705F38F3BF768518A1011C7EF974E9BEB322BCEB7BB4CE07A364E`；
- Manifest 与 `plugin.yml`：`0.1.2`；
- Gradle：`14/14`；
- 完整 `.NET` 解决方案：`725/725`；
- 安装器临时成功路径和故意失败自动回滚路径均通过。

## 生产部署

部署前确认 API `0.30.2`、数据库和迁移 `28/28` 正常，等级命令没有 `Pending` 或
`Claimed`，大厅 `25566` 没有已建立连接。LuckPerms 为 MySQL 存储、SQL messaging、
`auto-push-updates=true` 和 `parents-by-weight`。

安装器先创建
`E:\manual-backups\luckperms-tier-agent-20260814T005515Z`，校验并切换 JAR 和配置；
安装阶段五个 Java PID 全部不变。随后通过既有控制台桥执行 `save-all flush` 和 `stop`，
只启动计划任务 `Hechao-Server-Lobby`。大厅 PID 从 `7924` 切到 `9480`，其余四个 Java
进程的 PID、启动时间和路径未变化。日志确认 `0.1.2` 完成加载、启用并到达 `Done`；
自动回滚未触发。PID 只作为当次部署证据，后续状态必须实时复核。

部署后的 `01:08`、`01:13`、`01:18 UTC` 三轮五分钟只读同步全部成功，每轮读取
117 条 LuckPerms stored primary group，更新 14 条已绑定身份；等级命令队列保持为空，
代理没有新增错误。API、数据库和其他 Java 进程保持正常。

## 最终只读复核

2026-08-14 09:46 CST 再次通过 SSH/PowerShell 7 只读核验：大厅插件目录只存在一个
`0.1.2` JAR，大小和 SHA-256 与正式制品一致；大厅仍由 PID `9480` 监听回环端口，五个
Java PID 均未变化且没有已建立的大厅连接。同步计划任务最近一轮返回 `0`，API
`healthz` / `readyz` 均为 `200`，服务 `NRestarts=0`，当日 warning 及以上日志为 `0`。
PostgreSQL 中活动等级命令为 `0`，117 条快照与已绑定身份差异为 `0`，最新快照接收于
`01:43:07 UTC`。部署暂存目录已在精确路径校验后删除，生产 JAR 与正式回滚目录保持
存在。

## 剩余验收与回滚

最近十条历史等级命令涉及三名真实玩家，并非隔离测试账号。本次发布没有为了凑验收而
擅自修改、晋级或降级任何玩家，因此真实写链仍保留一个明确门槛：下一次管理员按正常
业务提交等级变更后，必须核对 MariaDB stored primary group、API 身份、权限和至少两轮
五分钟同步不回退；授权测试账号到位后再补完整正向、反向和拒绝路径。

加载失败或真实变更再次回退时，只优雅停止内部大厅，从上述回滚目录恢复 `0.1.1` JAR
和配置，再启动大厅。不得改写或删除 PostgreSQL 历史命令和审计；Velocity、生存服、
活动服及其他 Java 进程不属于回滚范围。

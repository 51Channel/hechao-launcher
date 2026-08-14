# LuckPerms Tier Agent 0.1.2 候选发布

- 候选版本：`HechaoLuckPermsTierAgent 0.1.2`
- 目标生产 API：`0.30.2-20260811T124943Z`
- 数据库迁移：无
- 激活要求：只重启内部大厅，不操作 Velocity、生存服或活动服

## 根因

2026-08-14 的生产只读诊断确认，最近 `10` 条等级命令全部被 `0.1.1` 回执为
`Applied`，但相关 PostgreSQL 身份和 LuckPerms 快照随后都恢复为旧值。每五分钟运行的
只读同步稳定读取 MariaDB，并按 stored primary group 更新后台身份，所以回退时间与同步
周期一致。

owl5 使用 `primary-group-calculation: parents-by-weight`。LuckPerms 官方 API 明确说明，
`User#getPrimaryGroup()` 返回按配置计算的主组，不一定等于 `User#setPrimaryGroup` 操作的
stored value。`0.1.1` 在计算主组已经等于目标时提前返回成功；目标继承节点已存在但
`players.primary_group` 仍为旧值的脏状态因此不会被修复。

此外，`0.1.1` 保存用户后没有调用 `MessagingService.pushUserUpdate`。其他 LuckPerms
实例可能继续持有旧用户缓存，后续保存时存在覆盖新状态的风险。

## 修复

- 始终使用 `UserManager.loadUser` 从存储读取用户，不以单服已加载缓存作为写入基线；
- 不再使用计算主组判断 stored value 冲突；无论计算结果是预期组、目标组还是第三组，均
  执行受控收敛。并发保护继续由 API 入队事务、快照修订和单玩家待处理约束承担；
- 先确保目标全局继承节点存在，清除其他受管全局组节点，再调用
  `User#setPrimaryGroup`，避免 LuckPerms 因用户尚未属于目标组而拒绝 stored value 更新；
- 节点清理失败时恢复本机内存中的已改节点；带上下文节点和非等级业务组保持不变；
- 等待 `saveUser` 完成后调用 `pushUserUpdate`，让其他 SQL messaging 实例重新加载；
- 保存或广播异常时立即从存储重新加载本机用户，避免大厅缓存停留在半修改状态；
- messaging service 不可用、节点写入失败、stored value 更新失败、保存失败或广播抛错时
  均故障关闭，不回执 `Applied`；
- 版本、Manifest、`plugin.yml` 和原子安装目标统一升级为 `0.1.2`。

## 候选验证

- Temurin `21.0.12+8`；
- Gradle `clean test jar --no-daemon`：`14/14` 通过；
- 完整 `.NET` 解决方案：`725/725` 通过；
- JAR：`326,515` 字节；
- SHA-256：`917984C1DED705F38F3BF768518A1011C7EF974E9BEB322BCEB7BB4CE07A364E`；
- Manifest `Implementation-Version` 与 `plugin.yml` 均为 `0.1.2`；
- 行为已与 LuckPerms 官方 `v5.5` 的 `ApiUser#setPrimaryGroup`、SQL `saveUser` 和
  `StorageAssistant.save` 调用顺序交叉核对。

## 发布与验收

1. 确认等级命令队列没有 `Pending` 或 `Claimed`，内部大厅无人，记录全部 Java PID。
2. 使用安装脚本备份 `0.1.1` JAR 和受限配置，原子写入 `0.1.2`；安装阶段不重启。
3. 只重启内部大厅，确认其他 Java PID 和 Velocity 路由未变化。
4. 确认日志加载 `HechaoLuckPermsTierAgent 0.1.2`，轮询无固定错误码。
5. 通过后台对授权测试账号执行正向等级变更，直接读取 MariaDB stored primary group，
   并等待至少两轮五分钟快照确认后台身份不再恢复。
6. 通过后台执行反向恢复，再完成一次 stored value、快照、权限和审计验收。

## 回滚

插件加载失败、轮询异常、目标服出现缓存回退或真实往返不通过时，只停止内部大厅，从本次
`E:\manual-backups\luckperms-tier-agent-*` 恢复 `0.1.1` JAR 与配置，再启动内部大厅。
PostgreSQL 无迁移，不执行数据库回滚；历史命令和审计不得改写或删除。

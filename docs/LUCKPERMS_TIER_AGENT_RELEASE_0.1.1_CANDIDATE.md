# LuckPerms Tier Agent 0.1.1 候选发布

- 候选版本：`HechaoLuckPermsTierAgent 0.1.1`
- 目标生产 API：`0.28.7-20260807T072043Z`
- 数据库迁移：无
- 激活要求：必须获得明确授权后重启内部大厅

## 根因

2026-08-07 20:06 至 20:08 CST 的生产只读诊断显示：

- 等级命令共 `5` 条，全部为 `Applied`，没有 `Pending`、`Claimed`、`Conflict` 或
  `Failed`；最近命令均由 `owl5-lobby` 一次领取并在数秒内回执；
- 最近发生变更的 `3` 位玩家，其赫朝等级、身份主组和 LuckPerms 快照随后全部回到旧值；
- `Administrator -> owner` 回到 `Collaborator / admin`，两次
  `Member -> Participant` 回到 `Member / default`；诊断未读取或记录玩家名、UUID、
  用户 ID 和管理员身份；
- `0.1.0` 只替换继承节点，然后以计算得到的 `User#getPrimaryGroup()` 判断成功，没有调用
  `User#setPrimaryGroup` 更新 LuckPerms 的持久化 stored primary group。下一分钟只读桥从
  `luckperms_players.primary_group` 读取旧值，因而覆盖 API 的短暂成功快照。

## 修复

- 在确认预期主组未冲突后，先调用 `User#setPrimaryGroup(target)`；
- LuckPerms 拒绝 stored primary group 更新时，回执固定错误码
  `primary_group_update_failed`，不替换节点、不保存用户；
- stored primary group 更新成功后，继续只替换 `default`、`vip`、`admin`、`owner` 四个
  无上下文继承节点，并通过 LuckPerms API 保存用户；
- 版本、`plugin.yml` 和安装目标统一升级为 `0.1.1`，不覆盖 `0.1.0` 正式制品；
- 增加单元测试，防止后续再次遗漏 `setPrimaryGroup`。

## 候选验证

- owl5 隔离构建目录：
  `E:\codex-build\luckperms-tier-agent-0.1.1-20260807T122231Z`；
- Gradle `clean test jar --no-daemon`：`6/6` 通过；
- JAR：`325,169` 字节；
- SHA-256：`F3B8871D55914CD403987A4AAEF901F1AF6FC12B44395FCACDFE99BB8C0AA450`；
- Manifest `Implementation-Version` 与 `plugin.yml` 均为 `0.1.1`；
- 构建只使用新建临时目录和构建 Java 进程，没有操作大厅、Velocity 或其他游戏服进程。

## 发布与验收

1. 确认命令队列中没有 `Pending` 或 `Claimed`。
2. 使用安装脚本备份 `0.1.0` JAR 与受限配置，并原子写入 `0.1.1`；安装阶段不得重启。
3. 获得明确授权后只重启内部大厅，不操作 Velocity、生存服、活动服或其他后端。
4. 确认日志加载 `HechaoLuckPermsTierAgent 0.1.1`，且原有五个游戏 Java 进程中只有大厅
   PID 按预期变化。
5. 对授权测试账号执行 `default -> vip`，等待至少两轮只读快照后确认后台、
   `luckperms_players.primary_group` 和实际权限均保持 `vip`。
6. 执行 `vip -> default` 恢复，并再次等待快照确认；没有完成往返读回前不得声明修复上线。

## 回滚

若插件加载失败或真实往返不通过，停服后从本次
`E:\manual-backups\luckperms-tier-agent-*` 恢复 `0.1.0` JAR 与配置，再只启动内部大厅。
PostgreSQL 无迁移，不需要数据库回滚；历史 `Applied` 命令不得改写或删除。

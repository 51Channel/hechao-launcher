# LuckPerms Tier Agent 0.1.1 候选发布

- 候选版本：`HechaoLuckPermsTierAgent 0.1.1`
- 目标生产 API：`0.28.7-20260807T072043Z`
- 数据库迁移：无
- 激活状态：已获授权并只重启内部大厅；真实玩家往返读回待完成

## 根因

2026-08-07 20:06 至 20:08 CST 的生产只读诊断显示：

- 等级命令共 `5` 条，全部为 `Applied`，没有 `Pending`、`Claimed`、`Conflict` 或
  `Failed`；最近命令均由 `owl5-lobby` 一次领取并在数秒内回执；
- 存在最新 `Applied` 记录的 `3` 位玩家，其赫朝等级、身份主组和 LuckPerms 快照随后
  全部回到旧值；
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

## 生产安装状态

2026-08-07 21:05 CST 已执行无重启安装：

- 生产磁盘 JAR：
  `E:\LobbyServer\plugins\HechaoLuckPermsTierAgent-0.1.1.jar`；
- 大小与 SHA-256 和候选制品完全一致；
- `0.1.0` JAR 与原配置已备份到
  `E:\manual-backups\luckperms-tier-agent-20260807T130519Z`，旧 JAR 哈希仍为
  `35A9BBB17620DC2FD7245E0EA8CCAA293DC98C264DA3463AB706846ED7E42A7B`；
- 新配置 ACL 继承关闭，仍只有 `3` 条受限访问规则，备份配置存在；
- 安装前后 Java PID 均为 `2576,6008,7748,9428,10412`，没有启动、停止或重启任何
  Java 进程；等级命令队列为空，API `0.28.7` 健康与就绪均正常；
- 该次无重启安装完成时，大厅进程仍加载内存中的 `0.1.0`。磁盘安装不等于修复上线，
  后续激活结果见下一节。

## 生产激活状态

2026-08-07 21:26 至 21:42 CST 已完成只重启内部大厅的激活检查：

- 原生 `minecraft:list` 返回 `0 / 200`，端口 `25566` 没有已建立连接；
- 控制台桥执行 `save-all flush`，本轮新增日志同时出现 `Saving the game` 与
  `Saved the game`；随后执行 `stop`，旧大厅 PID `6008` 正常退出且未使用中断兜底；
- 启动计划任务 `Hechao-Server-Lobby` 后，新大厅 PID 为 `7924`，只监听
  `127.0.0.1:25566`；其他 Java PID `2576,7748,9428,10412` 全部保持不变；
- 新日志确认加载 `HechaoLuckPermsTierAgent v0.1.1`、输出
  `LuckPerms tier agent enabled as owl5-lobby`，并完成 Paper `Done`；
- 等级代理日志没有 warning/error，最近五次命令领取请求均返回 HTTP `200`；
- API `0.28.7` 健康与就绪均为 `200`、数据库为 `ready`，等级命令
  `Pending / Claimed` 均为 `0`，重启后没有新增 `Failed / Conflict`，API journal
  warning/error 为 `0`；
- Paper 仍输出既有的离线模式、更新提示、SQLite 原生库清理与 Essentials 版本告警，
  但均不来自等级代理，且服务正常到达 `Done`。这些既有问题不纳入本次紧急修复范围。

插件运行和控制面链路已经通过，尚未把修复声明为业务闭环。下一门槛仍是对授权测试账号
执行一次正向改级，等待至少两轮 LuckPerms 只读快照，再反向恢复并重复确认。

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

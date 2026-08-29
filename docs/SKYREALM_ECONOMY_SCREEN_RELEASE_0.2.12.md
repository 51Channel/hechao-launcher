# 天域远征工业季第三方屏幕 0.2.12 服务端热修

- 发布日期：`2026-08-29`
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.12.jar`
- 网络协议：`3`，现有客户端兼容
- 构建来源提交：`e2c7fe0aee627aa0d89fca0d6fadf8aa099eb245`
- 变更范围：`activity-survival` 服务端 Screen JAR

## 事故证据

服务器在 `2026-08-28 18:11:36` 和 `2026-08-29 13:11:04` 连续生成
`Watching Server` 崩溃报告。两次均为 Screen `0.2.11` 的同一条调用链：

```text
ServerChunkCache.getChunk
Level.getChunkForCollisions
BlockCollisions.getChunk
CollisionGetter.noCollision
RtpSafeLocationFinder.isSafe
RtpSafeLocationFinder.findLoaded
RtpTeleportService.chunkReady
```

`0.2.11` 只保证候选 `LevelChunk` 通过 Future 异步完成。落点检查中的
`level.noCollision(player, targetBox)` 仍会扫描碰撞箱附近区块；需要相邻区块时，主线程
同步等待区块任务，而区块任务又需要主线程继续调度，最终被 60 秒 Watchdog 关闭。

实时诊断排除了资源耗尽：主机物理内存 `18GB`、诊断时可用约 `13.32GB`，提交限制仍余
约 `12.2GB`，E 盘健康且可用约 `34.11GB`；未发现 `OutOfMemoryError`、`hs_err_pid`、
heap dump 或 Windows Java 崩溃事件。

## 修复内容

- 删除 RTP 落点检查中的 `level.noCollision`，不再从世界对象触发邻区块同步加载；
- 脚部、头部和支撑方块只从 Future 返回的 `LevelChunk` 读取；
- 支撑碰撞形状使用 `LevelChunk` 作为 `BlockGetter`；
- 将玩家当前碰撞箱平移到候选落点，只有完整位于已验证为空气的两格柱体内才接受；
- 碰撞箱越界时放弃候选并继续异步查找，不加载相邻区块；
- 保持最大范围 `5000`、边界内缩 `32`、最小范围 `64`、冷却 `60` 秒、最多 `48` 个
  候选和总超时 `30` 秒不变。

本版没有修改网络负载、菜单 UI 或协议。客户端档案不需要变更，玩家无需重下客户端。

## 构建与部署

PowerShell 7、Java 21、Gradle 9.5.1 连续两次执行 `clean test build --no-daemon`：

- 测试：`117/117`，失败 `0`；
- JAR：`998,677` 字节；
- 两次 SHA-256：
  `DB9AA15D1851CF3E23E53F3411CF2CF03BF508F9334BA8F06E432C077F872471`；
- `git diff --check`：通过。

部署前确认玩家 `0/100`、后端已建立连接 `0`，执行 `save-all flush` 后正常停止。完整冷备份：

`E:\manual-backups\activity-survival-rtp-0.2.12-20260829T185800`

备份包含 `2,968` 个文件、`620` 个目录和 `2,523,290,230` 字节；源与备份的相对路径、
长度、文件数、目录数和总字节完全一致。旧版 JAR 保留在完整备份和独立暂存目录中。

原子替换后仅启动 `Hechao-Server-activity-survival`：

- 计划任务 `Running`，Java PID `7892`；
- `127.0.0.1:25600` 单监听；
- 唯一 Screen JAR 为 `0.2.12`，线上摘要与本地构建一致；
- Arclight 在 `4.216s` 完成启动；
- 新崩溃报告 `0`，启动致命签名 `0`；
- 空服 TPS `20.0`；最近一分钟 tick 耗时最小 `1.9ms`、中位 `2.5ms`、95 分位
  `3.3ms`、最大 `34.1ms`。

运行 `517` 秒后的二次回查保持同一 PID：Done 后卡服警告 `0`、RTP 错误 `0`、新崩溃
`0`。期间 1 名真实玩家正常进入。Create 初始化玩家侧配方/蓝图环境时记录 5 条
`Item must not be minecraft:air`，但修复前的 `2026-08-29-2.log.gz` 和
`2026-08-28-1.log.gz` 已分别存在 `90` 和 `7` 条同签名；它是既有非致命问题，不属于
本次 RTP Watchdog 回归。1 名玩家在线时 TPS 仍为 `20.0`，最近一分钟 tick 中位
`19.8ms`、95 分位 `24.2ms`、最大 `39.1ms`。

启动期仍有整合包既有的 dedicated-server 客户端 Mixin、TAB 注入、Essentials 非正式版本
和在线更新检查超时警告。这些签名在修复前已存在，且没有进入 RTP/Watchdog 调用链。

## 回滚与待验收

完整回滚源为上述冷备份；快速回滚 JAR 为：

`E:\manual-staging\hechao-economy-screen-0.2.12-20260829T185900\HechaoEconomyScreen-NeoForge-1.21.1-0.2.11.replaced.jar`

部署脚本已设置启动失败自动恢复 `0.2.11`；本次所有门禁通过，未执行回滚。

仍需真人完成：

1. 主世界和下界连续 RTP，确认落点安全且不会进入下界基岩顶；
2. 多名玩家同时 RTP、分散跑图并持续操作 Create/Sable 内容；
3. 观察 TPS/MSPT、GC、掉线和新崩溃报告；
4. 确认没有新的 `BlockCollisions.getChunk`、`ServerChunkCache.getChunk` 或
   `RtpSafeLocationFinder` Watchdog 栈。

结构化证据见
[`evidence/SKYREALM_ECONOMY_SCREEN_0.2.12_HOTFIX_2026-08-29.json`](evidence/SKYREALM_ECONOMY_SCREEN_0.2.12_HOTFIX_2026-08-29.json)。

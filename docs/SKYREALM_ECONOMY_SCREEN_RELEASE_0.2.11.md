# 天域远征工业季第三方屏幕 0.2.11 服务端热修

- 发布日期：`2026-08-25`
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.11.jar`
- 网络协议：`3`，与客户端 Screen `0.2.10` 兼容
- 构建来源提交：`894a2e75d7c60d28ef3e91c5db02cec400cbc8fa`
- 配套客户端档案：继续使用 `skyrealm-industrial-neoforge-1.21.1 / 1.0.30`
- 变更范围：`activity-survival` 服务端 Screen 与 EssentialsSpawn 重生配置

## 事故根因

`2026-08-24 21:25:09` 的崩溃报告显示，末影龙战斗与结算没有抛出致命异常。真正的
Watchdog 堆栈来自随后执行的 RTP：

```text
ServerChunkCache.getChunk
Level.getBlockState
RtpSafeLocationFinder.isSafe
RtpSafeLocationFinder.find
HechaoEconomyScreenMod.randomTeleport
```

Screen `0.2.10` 会在服务器主线程最多检查 `48` 个远处候选，并同步生成或加载未就绪区块。
单个 tick 达到 `60` 秒后，Watchdog 按 `max-tick-time=60000` 强制关闭服务器。因此“打完
龙后崩服”是时间上的关联，不是末影龙结算逻辑本身崩溃。

## 修复内容

- RTP 半径、边界和冷却保持不变：最大范围 `5000`、边界内缩 `32`、最小范围 `64`、
  玩家冷却 `60` 秒；
- 每次只请求一个候选区块，从专用守护线程调用 Minecraft 的 `getChunkFuture`，不在服务器
  主线程等待 Future；
- 候选区块用独立票据固定，Future 完成后回到服务器线程，只通过返回的 `LevelChunk`
  读取高度和方块，再执行碰撞与危险方块检查；
- 同一玩家不能并发提交 RTP；查找最多 `48` 个候选并有 `30` 秒总超时；
- 掉线、死亡、换维度、超时、加载失败和停服都会清理当前票据、请求状态和失败冷却；
- 成功传送使用原版 `POST_TELEPORT` 区块票据，速度和摔落距离归零；
- `plugins/Essentials/config.yml` 的 `respawn-listener-priority` 从 `high` 改为 `none`，让
  原版床、重生锚和世界出生点处理重生。`respawn-at-home` 保持 `false`，没有引入死亡后
  自动回第一个 home 的额外行为。

本版没有修改网络负载、菜单协议、客户端 UI 或客户端资源。服务端与现有 `0.2.10` 客户端
继续使用协议 `3`，因此 Test 档案保持 `1.0.30 / r23`，没有生成新清单或上传 OSS 对象。

## 构建与部署证据

PowerShell 7、Java `21.0.11`、Gradle `9.5.1` 连续两次执行
`clean test build --no-daemon`：

- 测试：`116/116`，失败 `0`；
- JAR：`998,394` 字节；
- 两次 SHA-256 均为
  `90E55908673C0B8B47673AA13200CC09387E46BD368FA2F1B8B762A029979BD7`；
- `git diff --check`：通过。

服务端在崩溃停止状态下完成完整备份：

`E:\manual-backups\activity-survival-bed-rtp-0.2.11-20260825T000757`

备份包含 `1,519` 个文件、`523` 个目录、`1,205,524,893` 字节；源与备份的相对路径、
长度、SHA-256 和目录集合差异均为 `0`。随后冷替换唯一 Screen JAR，并原子更新 Essentials
配置，再只通过计划任务 `Hechao-Server-activity-survival` 启动。

`2026-08-25 00:13 CST` 回查：

- 计划任务 `Running`，Java PID `8092`；
- `127.0.0.1:25600` 单监听；
- Arclight、Essentials `2.21.0` 与 EssentialsSpawn `2.21.0` 均已加载；
- 日志明确加载 `Hechao Economy Screen 0.2.11`，唯一 JAR 大小和摘要正确；
- `Done (4.543s)`；
- Screen/RTP 专属错误 `0`，重生配置错误 `0`；
- 当前错误签名相对部署前备份新增 `0`，`Done` 后 ERROR/FATAL/Watchdog 为 `0`；
- 最新崩溃报告仍为部署前的 `crash-2026-08-24_21.25.09-server.txt`。

`00:16:40 CST` 的第二次稳定性回查保持同一 Java PID，连续运行 `377` 秒；空服主机 CPU
约 `1.22%`、工作集约 `965.7 MiB`，`Done` 后卡服警告 `0`，新崩溃报告 `0`。

启动时仍会出现整合包既有的 dedicated-server 客户端 Mixin、Essentials 非正式兼容版本和
更新检查联网超时日志；这些签名在部署前备份中已经存在，不属于 `0.2.11` 新增问题。

## 回滚与待验收

回滚目标为上述完整备份中的 Screen `0.2.10` 和原 Essentials 配置。若启动或真人验收
出现新回归，应先正常停止 `activity-survival`，再用完整备份恢复整个槽位，不能只热替换
JAR 或配置。

自动验证不能代替以下真人门禁：

1. 白天和夜间点击床后死亡，确认回到床边；破坏床后确认回到世界出生点；
2. 主世界与下界执行 RTP，确认不会卡主线程、不会落到下界基岩顶、重复点击被拒绝；
3. RTP 期间掉线、换维度和超时后重新执行，确认票据与冷却已释放；
4. 多人同时 RTP 时观察 TPS/MSPT，并确认没有新 Watchdog 或区块加载堆栈。

真人门禁完成前不推进 Gray 或 Production。结构化证据见
[`evidence/SKYREALM_ECONOMY_SCREEN_0.2.11_HOTFIX_2026-08-25.json`](evidence/SKYREALM_ECONOMY_SCREEN_0.2.11_HOTFIX_2026-08-25.json)。

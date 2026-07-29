# 状态采集器 0.2.1

## 1. 目的

NeoForge 活动服在零玩家持续 60 秒后会暂停世界 Tick。端口、Java 进程和状态查询
仍正常，但 Tick 指标文件不会刷新。`0.2.0` 将该正常休眠误报为
`MetricsFileStale`。

`0.2.1` 增加逐目标配置 `allowStaleMetricsWhenEmpty`。只有目标在线、人数为 `0`、
配置明确启用且最后一份指标文件结构与数值均有效时，采集器才把它表示为“空服暂停”：

- 不上报过期 TPS、MSPT、GC 或伪造的新时间戳；
- 不上报 `MetricsFileStale`；
- 一旦出现在线玩家，过期指标立即恢复为 `MetricsFileStale`；
- 文件缺失、无效、未配置、状态离线和进程异常仍照常上报。

## 2. 制品

| 项目 | 值 |
| --- | --- |
| 版本 | `0.2.1` |
| FileVersion | `0.2.1.0` |
| 大小 | `73,829,052` 字节 |
| SHA-256 | `7645909E8FE9690D022D7B14E065ACACAB85FA39F4D2C03B8E52BFBF9F3899ED` |

## 3. 部署

`tools/server/Install-HechaoStatusCollector.ps1` 使用严格主机密钥校验上传制品，停用并
等待只读采集任务空闲，备份 EXE 与 JSON，原子替换后执行手工上报和计划任务上报。
失败时恢复旧 EXE、旧配置和原任务状态。

两台游戏 VPS 已部署同一哈希：

- `owl5`：只为 `activity` 启用空服暂停策略；
- `owl9`：保持恐怖整蛊单目标原配置，未触碰真正 PVP。

两机部署前后的 Java PID 集合完全一致。没有启动、停止或重启 Minecraft、
Velocity 或 API。部署过程中的三个故意失败路径均恢复到 `0.2.0` 后才继续。

## 4. 验收

定向测试 `12/12` 通过，覆盖有效新鲜指标、有效过期指标、空服暂停接受、有人在线
拒绝过期指标、损坏和缺失文件，以及单目标失败隔离。

生产 60 秒预检中：

- Activity 在线 `0` 人，`tickMetricsState=paused-when-empty`，探针问题为空；
- Lobby 始终 `0` 人；
- API 健康与就绪探针 `22/22`，p95 `180.846 ms`；
- 最终活动 Critical 为 `0`。

Survival1 已按真实停服状态改为 `Closed`，其离线 Critical 在下一评估周期自动恢复；
当前仅保留 owl9 磁盘 Warning。机器证据见
[`evidence/STATUS_COLLECTOR_0.2.1_DEPLOYMENT_2026-07-30.json`](evidence/STATUS_COLLECTOR_0.2.1_DEPLOYMENT_2026-07-30.json)
和
[`evidence/GRAY_READINESS_PREFLIGHT_2026-07-30.json`](evidence/GRAY_READINESS_PREFLIGHT_2026-07-30.json)。

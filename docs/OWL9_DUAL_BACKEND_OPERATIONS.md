# owl9 双服务端边界

> 2026-08-01 状态采集边界：`pvp` 仍只代表 `C:\mc\server` 的恐怖整蛊服，
> `pvp-purpur` 只代表 `E:\MinecraftServer` 的真正 PVP。两者共享 `25565`，
> 状态采集器必须同时核对监听 PID 的 `java.exe` 路径，禁止仅凭端口把运行状态
> 归给 `pvp`。生产配置见
> [`../deploy/windows/server-heartbeats/server-heartbeats.owl9.production.json`](../deploy/windows/server-heartbeats/server-heartbeats.owl9.production.json)。

> 只读复核时间：2026-07-28 15:20（Asia/Shanghai）
>
> 这是一条强制运维边界。owl9 上的“恐怖整蛊服”和“PVP 服”是两个不同的
> Minecraft 服务端，任何启停、部署、备份、监控或验收都必须先按本表核对。

## 1. 唯一映射

| 项目 | 恐怖整蛊服 | PVP 服 |
| --- | --- | --- |
| 服务端根目录 | `C:\mc\server` | `E:\MinecraftServer` |
| 核心 | Fabric `1.20.1` | Purpur `1.21.11-2568-f57bd86` |
| 启动入口 | 计划任务 `HorrorPrank` / `start-headless.bat` | `start.bat`，当前没有专属计划任务 |
| Java | `C:\mc\jre\jdk-21.0.11+10-jre` | `E:\MinecraftServer\jdk` |
| 内存 | `-Xms2G -Xmx5G` | `-Xms2G -Xmx4G` |
| 本机端口 | `25565` | `25565` |
| `online-mode` | `true` | `false` |
| 目录显示名 | `恐怖整蛊` | 当前未接入赫朝启动器目录 |
| 历史内部标识 | server ID / Velocity target 为 `pvp`，档案为 `pvp-fabric-1.20.1` | 无当前目录 ID、Velocity target 或客户端档案 |
| 2026-07-28 13:24 状态 | 运行中，PID `7216` | 已停止 |

`pvp` 是恐怖整蛊服在赫朝启动器平台中的历史内部别名，不代表
`E:\MinecraftServer` 里的真正 PVP 服。现有文件名、数据库 ID、Velocity 目标和
证据文件为了兼容暂不重命名，但所有面向人的文档和操作记录必须写成
“恐怖整蛊（历史内部标识 `pvp`）”。

## 2. 共享入口约束

两个服务端都绑定 owl9 本机 `25565`，并复用公网
`owl9.vipi9.top:19243`。因此：

1. 两个服务端绝不能同时启动。
2. 启动前必须同时检查 Java 进程、`25565` 监听、目标目录和核心类型。
3. 恐怖整蛊服运行时，Velocity 目标 `pvp`、启动器目录“恐怖整蛊”和
   `pvp-fabric-1.20.1` 才构成正确组合。
4. 真正 PVP 服运行时，当前赫朝启动器中的“恐怖整蛊”入口不得保持可进入状态；
   在为 PVP 建立独立目录记录、客户端档案和切换流程前，不把它纳入本项目验收。
5. 状态采集器 `0.2.2` 同时登记 `pvp` 与 `pvp-purpur`，并在读取共享 `25565` 前
   核对监听 PID 的 `java.exe` 路径。恐怖整蛊只接受
   `C:\mc\jre\jdk-21.0.11+10-jre\bin\java.exe`，真正 PVP 只接受
   `E:\MinecraftServer\jdk\bin\java.exe`；路径不匹配时不得上报另一目标的数据。

## 3. 操作前检查

每次对 owl9 做写操作前至少确认：

```text
目标名称
服务端根目录
核心与 Minecraft 版本
启动入口
当前 Java 可执行文件
25565 监听 PID
Velocity / 启动器逻辑目标
备份目标目录
```

目录、核心或启动入口任意一项不匹配时立即停止，不用“PVP”这个模糊简称继续操作。

## 4. 当前项目范围

赫朝启动器当前发布的 Fabric `1.20.1` 客户端、modern forwarding、CrossStitch、
真实进服、跨版本回程候选和 TPS/MSPT/GC 验收，全部属于恐怖整蛊服
`C:\mc\server`。真正 PVP 服 `E:\MinecraftServer` 仍是独立服务端，没有玩家目录、
Velocity 目标或客户端档案，不属于上述玩家进服验收。

运维侧已经单独接入真正 PVP：2026-07-31，管理员通过服控目标 `pvp-purpur` 成功启动
该服务端；2026-08-01，状态采集器 `0.2.2` 按 Java 路径确认其进程、玩家、CPU、内存和
磁盘数据。`HechaoServerMetrics-0.1.0.jar` 已预置但没有为此重启服务端，TPS/MSPT/GC
仍等待下一次管理员正常手动重启后生效。这些运维证据不代表真正 PVP 已向玩家开放，
实时运行状态也必须在每次操作前重新核验。详见
[`SERVER_CONTROL_AGENT_OPERATIONS.md`](SERVER_CONTROL_AGENT_OPERATIONS.md) 与
[`STATUS_COLLECTOR_RELEASE_0.2.2.md`](STATUS_COLLECTOR_RELEASE_0.2.2.md)。

## 5. 恐怖整蛊运行与备份验收

2026-07-28 在玩家数为 `0` 时，恐怖整蛊服通过控制台
`save-all flush`、`save-off`、VSS 快照和 `save-on` 生成正式世界归档：

- 归档：`E:\backups\horrorprank-backup-20260728-142039.zip`
- 字节：`4,149,156,327`
- 条目：`2,493`
- SHA-256：
  `50FBC949071EB08D828D4A53F8AF001C8AC5AAF9A42443083A28714B8D32975A`
- Java PID 在备份前后均为 `7216`，没有停止或重启服务端。
- `active.json`、`.partial` 和 VSS 卷影均已清理。

异机副本已完整恢复到
`H:\server-backups\owl9\formal-world-acceptance\2026-07-28\horrorprank`。
本地重新计算归档哈希一致，全部 `2,493` 个条目完成长度与 SHA-256 逐文件比对，
无缺失或额外文件；`level.dat` 有效且没有 `session.lock`。

全量扫描 `2,370` 个 `.mca` 时发现源世界本身有 `22` 个零字节辅助占位文件，
其中 `entities` 为 `14` 个、`poi` 为 `8` 个，地形 `region` 为 `0` 个。
恢复副本与 `C:\mc\server\world` 的相对路径清单完全一致。验证器默认仍拒绝空区域
文件；本次只有在显式提供这 `22` 个源端路径后才接受，且全部白名单项必须被使用。
其余 `2,348` 个非空区域文件和共 `1,403,353` 个区块记录全部通过结构检查。

15 秒备份后运行样本为 `20/20/20 TPS`、`12.591603775 MSPT`、GC 增量
`0 ms`；PID 和 `25565` 监听均为 `7216`，真正 PVP 进程数为 `0`。

`E:` 当前只剩 `5,787,840,512` 字节，不能安全同时保留当前归档和下一份最坏情况
临时归档。当前归档继续保留；再次完整备份前必须先验证异机副本并计划扩容或精确回收
当前远端归档，不能让脚本提前删除唯一可用备份。

机器可读盘点见
[`evidence/OWL9_DUAL_BACKEND_MAPPING_2026-07-28.json`](evidence/OWL9_DUAL_BACKEND_MAPPING_2026-07-28.json)，
运行与备份证据见
[`evidence/OWL9_HORRORPRANK_RUNTIME_AND_WORLD_BACKUP_2026-07-28.json`](evidence/OWL9_HORRORPRANK_RUNTIME_AND_WORLD_BACKUP_2026-07-28.json)。

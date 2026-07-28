# 服务器运行指标运维

> 当前生产：API `0.20.2-20260727T225819Z`、Windows 采集器 `0.2.0`
>
> 已加载：Paper/Purpur、NeoForge 与 Fabric 指标代理 `0.1.0`
>
> 安全边界：只读；不包含服务器启动、停止、重启、RCON 或控制台命令
>
> owl9 命名边界：指标目标 `pvp` 和采集器 `owl9-pvp` 只采集
> `C:\mc\server` 的恐怖整蛊 Fabric 服，不代表
> `E:\MinecraftServer` 的真正 PVP Purpur 服。

## 1. 数据链路

Windows 采集器每分钟按监听端口定位本机 Java 进程，读取：

- 工作集与私有内存；
- 采样后的整机占比 CPU；
- 进程启动时间；
- 服务端数据盘总量与可用空间；
- Minecraft 状态、在线人数、协议和软件版本。

Paper/Purpur 指标代理每 5 秒在主线程读取 Paper 自带的 1/5/15 分钟 TPS 与平均
MSPT，并读取 JVM 累计 GC 时间。NeoForge/Fabric 代理只在服务端 tick 事件中记录
时间戳，每 100 tick 生成一次 1/5/15 分钟 TPS、1 分钟滚动 MSPT 和 JVM 累计 GC
快照；文件 IO 在单个守护线程完成。三种代理只把相同固定结构写入：

```text
plugins/HechaoServerMetrics/metrics.json
```

写入先完成同目录临时文件，再原子替换正式文件。代理没有命令、权限、玩家数据、网络
客户端或服务端控制能力。采集器只读取 30 秒内的新鲜快照；缺失、过期或损坏使用固定
问题代码上报，不上传异常正文或文件内容。

API 迁移 `16` 扩展当前心跳，并建立
`launcher.server_runtime_samples`。样本按目标与采集时间幂等，默认保留 30 天，
每 6 小时清理一次。管理后台“服务状态”显示：

- 心跳新鲜度、在线状态和玩家数；
- TPS、MSPT、进程内存、CPU、磁盘余量和运行时长；
- 当前固定探针问题；
- 最近 24 小时问题样本与涉及目标数。

管理页面仍不提供任何启停按钮。

## 2. 兼容范围

| 目标 | 进程/磁盘 | TPS/MSPT/GC | 当前边界 |
| --- | --- | --- | --- |
| `lobby` | Windows 采集器 | Paper/Purpur 代理 | 已加载并取得 TPS/MSPT/GC |
| `survival2` | Windows 采集器 | Paper/Purpur 代理 | 已加载并取得 TPS/MSPT/GC |
| `survival1` | Windows 采集器 | Paper/Purpur 代理 | 已加载并取得 TPS/MSPT/GC |
| `activity` | Windows 采集器 | NeoForge 1.21.11 代理 | 已加载并取得 TPS/MSPT/GC |
| `pvp`（恐怖整蛊历史目标） | `owl9` Windows 采集器 | Fabric 1.20.1 代理 | 已加载并取得 TPS/MSPT/GC |

没有指标代理不会影响在线状态或其他目标上报，只显示固定的“未配置/文件缺失”问题。

## 3. 构建与测试

```powershell
dotnet test Hechao.Launcher.sln -c Release
.\src\Hechao.VelocityAuthorizer\gradlew.bat `
    -p .\src\Hechao.ServerMetricsAgent clean test jar --no-daemon
& 'C:\Users\Administrator\.gradle\wrapper\dists\gradle-8.12-bin\53bzr39g899nuhyyt338y4z0e\gradle-8.12\bin\gradle.bat' `
    -p .\src\Hechao.ModServerMetricsAgent clean build --no-daemon
dotnet publish .\src\Hechao.StatusCollector\Hechao.StatusCollector.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o .\artifacts\publish\status-collector-win-x64
```

指标代理输出：

```text
src\Hechao.ServerMetricsAgent\build\libs\HechaoServerMetrics-0.1.0.jar
src\Hechao.ModServerMetricsAgent\fabric\build\libs\HechaoServerMetrics-Fabric-1.20.1-0.1.0.jar
src\Hechao.ModServerMetricsAgent\neoforge\build\libs\HechaoServerMetrics-NeoForge-1.21.11-0.1.0.jar
```

当前制品：

| 制品 | SHA-256 |
| --- | --- |
| `hechao-status-collector-0.2.0-win-x64.zip` | `30D9BC599B80FEF48D5FE02B340FE494BE8DE7B5D590828BED34F155D81F8167` |
| `HechaoServerMetrics-0.1.0.jar` | `BD03312007E043223B37CF634872C3DAA4C0FB11B80B54ADC546507853528B2C` |
| `HechaoServerMetrics-Fabric-1.20.1-0.1.0.jar` | `D38FB92413CC3B6B43CB87E396957697455A30799415611CB43C55D2C895B3F6` |
| `HechaoServerMetrics-NeoForge-1.21.11-0.1.0.jar` | `49C258C3AFF655070F40B576AC4A026AE8B5D43030A635800A7038451766027E` |

最新完整回归为 .NET `360/360`、Paper/Purpur 指标代理 `2/2`、模组指标公共逻辑
`6/6`。两个模组 JAR 连续干净构建的 SHA-256 一致。API 候选使用生产数据库副本在
独立端口验证迁移 16、心跳和样本幂等、管理汇总及既有签名发布链路。

## 4. 部署顺序

1. 备份生产 API、数据库、采集器目录和当前采集配置。
2. 使用生产数据库副本运行 API `0.19.0` 隔离验收，验证迁移 16、心跳幂等和 MFA
   管理汇总。
3. 原子部署 API，回归健康、就绪、目录、旧官网和中转 API。
4. 发布 Windows 采集器 `0.2.0`，补全本机 `dataPath`，手工只运行一次并核对上报。
5. 更新一分钟计划任务；不得启动任何 Minecraft 或 Velocity 进程。
6. 使用 `deploy/windows/server-metrics/Install-ServerMetricsAgent.ps1` 备份并复制
   JAR 到三个 Paper/Purpur `plugins` 目录。
7. 仅在活动服和恐怖整蛊服已关闭时，使用
   `deploy/windows/mod-server-metrics/Install-ModServerMetricsAgent.ps1` 校验端口、
   Loader 元数据和 SHA-256，再把对应 JAR 原子部署到 `mods`。
8. 保持游戏服原状态。服主以后自行启动对应服务端后，才验证代理加载和 JSON 新鲜度。

部署脚本最终必须输出：

```text
server_restart=not_performed
```

本轮生产部署已按上述顺序完成。采集器备份位于
`C:\ProgramData\Hechao\StatusCollector\backups\collector-0.2.0-20260727T004750Z`，
指标代理备份位于 `E:\manual-backups\server-metrics-20260727T004852Z`。部署前后 Java
PID 均为同一组，计划任务手工与自动运行均返回成功。

2026-07-28 又将 `pvp` 从 `owl5` 的远端状态探针拆到 `owl9` 本机只出站采集器。
`owl5` 配置现保留四个本机目标，`owl9-pvp` 只查询
`127.0.0.1:25565` 和 `C:\mc\server`。跨过两边完整计划周期后，API 中该行仍由
`owl9-pvp` 写入，任务返回 `0`，磁盘容量入库；部署前后恐怖整蛊 Java 进程与端口监听
均为空。证据见
[`evidence/OWL9_STATUS_COLLECTOR_DEPLOYMENT_2026-07-28.json`](evidence/OWL9_STATUS_COLLECTOR_DEPLOYMENT_2026-07-28.json)。

2026-07-28 已在停服状态把 NeoForge 代理静态部署到
`E:\ActivityNeoForge\mods`，把 Fabric 代理静态部署到 `C:\mc\server\mods`。
安装器复核目标端口、Loader 元数据和制品 SHA-256；每服只保留一个启用 JAR，部署
记录目录 ACL 已收紧，上传暂存已清理。活动服目标进程、恐怖整蛊全部 Java 进程和两个
监听端口在部署后仍为空，没有启动或重启游戏服。证据见
[`evidence/MOD_SERVER_METRICS_DEPLOYMENT_2026-07-28.json`](evidence/MOD_SERVER_METRICS_DEPLOYMENT_2026-07-28.json)。

## 5. 验证

数据库：

```sql
SELECT velocity_target, is_online, process_working_set_bytes,
       process_cpu_percent, disk_free_bytes, tps_1m, mspt_average,
       probe_issues, captured_at, received_at
FROM launcher.velocity_target_heartbeats
ORDER BY velocity_target;

SELECT velocity_target, count(*), max(received_at)
FROM launcher.server_runtime_samples
GROUP BY velocity_target
ORDER BY velocity_target;
```

Windows：

```powershell
Get-ScheduledTaskInfo -TaskName 'Hechao Launcher Server Heartbeats'
Get-Content -Raw 'E:\LobbyServer\plugins\HechaoServerMetrics\metrics.json'
```

第二条命令以及活动服/恐怖整蛊的同路径文件只在对应服务端已加载代理时存在；停服时文件
可以保留，但采集器会结合进程状态和新鲜度判断，不把旧快照当作在线指标。

2026-07-28 受控开服后的单用户空载基线：

| 目标 | TPS | MSPT | 累计 GC |
| --- | ---: | ---: | ---: |
| `lobby` | `20.003904` | `1.8530` | `394 ms` |
| `survival1` | `19.996649` | `1.1225` | `741 ms` |
| `survival2` | `20.000241` | `1.0375` | `512 ms` |
| `activity` | `20` | `5.7745` | `253 ms` |
| `pvp` | `20` | `12.7157` | `1413 ms` |

五个目标的进程、CPU、内存、启动时间、磁盘与 TPS/MSPT/GC 均已入库。该结果只证明
空载链路和代理正确，不代替 5 人、20 人或正式活动负载验收。

## 6. 回滚

- API 可回滚到 `0.18.0`；迁移 16 和历史表保留，不手工删除。
- 采集器可从备份恢复 `0.1.0`；新字段会被旧 API 忽略前必须先回滚 API。
- 指标代理回滚只在服务器关闭状态下移出 JAR，或等待下一次服主计划重启生效。
- NeoForge/Fabric 代理分别从本次受限备份目录恢复；回滚前必须再次确认目标端口和
  对应 Java 进程均已停止。
- 回滚不启动、停止或重启任何游戏进程。

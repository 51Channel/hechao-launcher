# 服务器运行指标运维

> 当前生产：API `0.19.0-20260727T005013Z`、Windows 采集器 `0.2.0`
>
> 已部署待服务端下次自行重启加载：Paper/Purpur 指标代理 `0.1.0`
>
> 安全边界：只读；不包含服务器启动、停止、重启、RCON 或控制台命令

## 1. 数据链路

Windows 采集器每分钟按监听端口定位本机 Java 进程，读取：

- 工作集与私有内存；
- 采样后的整机占比 CPU；
- 进程启动时间；
- 服务端数据盘总量与可用空间；
- Minecraft 状态、在线人数、协议和软件版本。

Paper/Purpur 指标代理每 5 秒在主线程读取 Paper 自带的 1/5/15 分钟 TPS 与平均
MSPT，并读取 JVM 累计 GC 时间。它只把固定结构写入：

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
| `lobby` | Windows 采集器 | Paper/Purpur 代理 | JAR 加载需要服主下一次自行重启 |
| `survival2` | Windows 采集器 | Paper/Purpur 代理 | JAR 加载需要服主下一次自行重启 |
| `survival1` | Windows 采集器 | Paper/Purpur 代理 | JAR 加载需要服主下一次自行重启 |
| `activity` | Windows 采集器 | 尚无 NeoForge 指标代理 | 关闭时报告进程未运行 |
| `pvp` | 仅状态协议 | 尚无远端主机指标 | 需要恢复 `owl9` 管理密钥 |

没有指标代理不会影响在线状态或其他目标上报，只显示固定的“未配置/文件缺失”问题。

## 3. 构建与测试

```powershell
dotnet test Hechao.Launcher.sln -c Release
.\src\Hechao.VelocityAuthorizer\gradlew.bat `
    -p .\src\Hechao.ServerMetricsAgent clean test jar --no-daemon
dotnet publish .\src\Hechao.StatusCollector\Hechao.StatusCollector.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o .\artifacts\publish\status-collector-win-x64
```

指标代理输出：

```text
src\Hechao.ServerMetricsAgent\build\libs\HechaoServerMetrics-0.1.0.jar
```

当前制品：

| 制品 | SHA-256 |
| --- | --- |
| `hechao-status-collector-0.2.0-win-x64.zip` | `30D9BC599B80FEF48D5FE02B340FE494BE8DE7B5D590828BED34F155D81F8167` |
| `HechaoServerMetrics-0.1.0.jar` | `BD03312007E043223B37CF634872C3DAA4C0FB11B80B54ADC546507853528B2C` |

生产前自动回归为 .NET `325/325`、指标代理 `2/2`。API 候选使用生产数据库副本在
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
7. 保持游戏服原状态。服主以后自行重启对应服务端后，才验证插件加载和 JSON 新鲜度。

部署脚本最终必须输出：

```text
server_restart=not_performed
```

本轮生产部署已按上述顺序完成。采集器备份位于
`C:\ProgramData\Hechao\StatusCollector\backups\collector-0.2.0-20260727T004750Z`，
指标代理备份位于 `E:\manual-backups\server-metrics-20260727T004852Z`。部署前后 Java
PID 均为同一组，计划任务手工与自动运行均返回成功。

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

第二条命令只有在服主自行重启并成功加载代理后才应存在。文件缺失不能作为擅自重启服务
器的理由。

当前生产已确认大厅、Survival1、Survival2 的进程内存、CPU、启动时间和磁盘容量入库；
活动服处于关闭状态，PVP 远端不可达。三个 Paper/Purpur 目标在下次服主自行重启前应
继续显示 `MetricsFileMissing`，这是预期状态。

## 6. 回滚

- API 可回滚到 `0.18.0`；迁移 16 和历史表保留，不手工删除。
- 采集器可从备份恢复 `0.1.0`；新字段会被旧 API 忽略前必须先回滚 API。
- 指标代理回滚只在服务器关闭状态下移出 JAR，或等待下一次服主计划重启生效。
- 回滚不启动、停止或重启任何游戏进程。

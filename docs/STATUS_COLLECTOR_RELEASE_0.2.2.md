# 状态采集器 0.2.2 发布记录

- 发布日期：2026-08-01
- 源码提交：`bdeef56c2a8a234f0166e5d2a1b721a3c17c9e9b`
- 正式标签：`status-collector-v0.2.2`
- 部署脚本修复：`68903eefbfe328bfe3a77631a2d2bbc3ba1605d9`
- 目标主机：owl9

## 修复内容

owl9 的恐怖整蛊服与真正 PVP 共用 `127.0.0.1:25565`。旧采集器只配置了
`pvp -> C:\mc\server`，因此真正 PVP 运行时，共享端口仍被错误记到 `pvp`，
而 `pvp-purpur` 没有心跳。

`0.2.2` 为采集目标增加 `expectedProcessExecutablePath`。采集器解析监听端口的
PID 后，必须确认其 `java.exe` 与目标配置一致，才会查询服务器状态并上报进程、
玩家和性能数据。owl9 现同时配置：

| 目标 | 服务端目录 | Java 归属 |
| --- | --- | --- |
| `pvp` | `C:\mc\server` | `C:\mc\jre\jdk-21.0.11+10-jre\bin\java.exe` |
| `pvp-purpur` | `E:\MinecraftServer` | `E:\MinecraftServer\jdk\bin\java.exe` |

## 生产结果

- `pvp`：离线，`ProcessNotRunning`，不再借用真正 PVP 的共享端口数据。
- `pvp-purpur`：在线，`0/20`，PID `2912`，CPU、内存和磁盘均已进入 API。
- 指标插件已预置到
  `E:\MinecraftServer\plugins\HechaoServerMetrics-0.1.0.jar`，未重启服务端；
  TPS/MSPT/GC 会在下一次正常手动重启真正 PVP 后开始生成。
- 心跳任务结果为 `0`，API `/healthz` 与 `/readyz` 均为 `200`。
- 部署前后 Minecraft PID 均为 `2912`，启动时间未变化，没有启动、停止或重启
  恐怖整蛊服与真正 PVP。

## 回滚

- 旧采集器：
  `C:\ProgramData\Hechao\StatusCollector\backups\collector-0.2.2-20260731T175135Z`
- 旧配置：
  `C:\ProgramData\Hechao\StatusCollector\backups\configuration-20260731T175506Z\server-heartbeats.json`

配置发布脚本在失败时恢复旧 JSON 并重新执行心跳任务，不控制 Minecraft 进程。
首轮原子替换因 Windows 要求显式备份路径而在替换前失败；旧配置哈希、PID 和任务
均保持不变。脚本修复后第二轮一次成功。

结构化证据见
[`evidence/STATUS_COLLECTOR_0.2.2_OWL9_DUAL_BACKEND_DEPLOYMENT_2026-08-01.json`](evidence/STATUS_COLLECTOR_0.2.2_OWL9_DUAL_BACKEND_DEPLOYMENT_2026-08-01.json)。

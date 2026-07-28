# owl9 双服务端边界

> 只读复核时间：2026-07-28 13:24（Asia/Shanghai）
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
5. 状态采集器当前使用 `velocityTarget=pvp` 和
   `dataPath=C:\mc\server`，只代表恐怖整蛊服。若改开真正 PVP 服，必须先停用或
   切换该逻辑目标，不能让 PVP 的 `25565` 状态冒充恐怖整蛊。

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
`C:\mc\server`。真正 PVP 服 `E:\MinecraftServer` 是独立服务端，本轮只完成
只读识别，不修改、不启动、不停止。

机器可读盘点见
[`evidence/OWL9_DUAL_BACKEND_MAPPING_2026-07-28.json`](evidence/OWL9_DUAL_BACKEND_MAPPING_2026-07-28.json)。

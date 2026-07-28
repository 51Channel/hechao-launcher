# 恐怖整蛊跨版本返回大厅设计（历史 PVP 标识）

> 状态：代码已实现、默认关闭；回环隔离代理的 1.13/1.20.1/1.21.11 status
> 协议协商、受限凭据探针及 API `0.21.0` 生产数据库副本验收均已通过，真实登录、
> `/hub` 和生产灰度仍待完成。
>
> 生产 Velocity 的 ViaVersion/ViaBackwards 仍为 `.disabled`，大厅目录开关仍为关闭，
> 本阶段没有重启生产代理或游戏服。
>
> 命名边界：本文文件名及历史内部标识 `PVP` / `pvp` 实际指 owl9 的恐怖整蛊
> Fabric `1.20.1` 服务端 `C:\mc\server`，不指真正的 PVP Purpur 服务端
> `E:\MinecraftServer`。本设计和当前验收均不得操作后者。

## 1. 问题

恐怖整蛊使用 Minecraft `1.20.1` Fabric 客户端，大厅使用 Minecraft `1.21.11`
Paper。现网 `HubCommand-1.0.0` 已由 Velocity 全局注册 `/hub`、`/lobby` 和 `/l`，
命令本身没有缺失；它会直接请求连接 `lobby`。当前失败边界有两层：

1. Velocity 没有启用协议转换插件，1.20.1 客户端不能直接进入 1.21.11 后端。
2. 赫朝授权 API 会对不同 Minecraft 版本返回 `MinecraftVersionMismatch`，并且该
   原因在 `monitor` 模式下也会立即拒绝。

因此不能靠放宽全部版本校验解决，也不能把当前 `/hub` 当作已经可用。

## 2. 选定方案

使用 Velocity 上的 ViaVersion + ViaBackwards 做协议转换，同时给服务器目录增加
目标服级别的 `AllowsProtocolTranslation` 开关：

- 数据库迁移 `018_protocol_translation_routes.sql` 增加
  `allow_protocol_translation boolean NOT NULL DEFAULT false`。
- 管理后台的服务器编辑页提供“允许跨版本协议转换”复选框。
- 只有目标服务器显式开启时，授权 API 才允许 Minecraft 版本不同的转服。
- 等级、停服状态、单服拒绝规则、正版绑定和会话来源仍按原顺序检查。
- 目标为 Fabric、Forge 或 NeoForge 时，客户端档案匹配仍会继续检查；协议转换开关
  不能绕过模组档案保护。

生产目标只计划对 `lobby` 开启。这样恐怖整蛊可以返回大厅，但大厅不能用基础客户端
跨版本进入恐怖整蛊，其他服务器也不会自动接受不同版本。

## 3. 现有制品核对

owl5 已有以下禁用制品，尚未改名或加载：

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `ViaVersion-5.11.0.jar.disabled` | 6,329,567 | `89DB76C8E3E674238F5EEE2BB7A9E9A2BEEBA0760BBD1B86494778E8A5A52F70` |
| `ViaBackwards-5.11.0.jar.disabled` | 1,378,644 | `41085A59D784C9A0D14917FE7487EF5E201A9DA7825FD047F08D328FF33EECDC` |

JAR 内 `velocity-plugin.json` 均声明版本 `5.11.0`，ViaBackwards 明确依赖
ViaVersion。实际类目录包含从 `1.20` 到 `1.21.11` 的完整逆向协议链，包括
`v1_20_2to1_20`、`v1_20_3to1_20_2`、`v1_20_5to1_20_3`、
`v1_21to1_20_5`、`v1_21_2to1_21`、`v1_21_4to1_21_2`、
`v1_21_5to1_21_4`、`v1_21_6to1_21_5`、`v1_21_7to1_21_6`、
`v1_21_9to1_21_7` 和 `v1_21_11to1_21_9`。

类存在只证明制品具备转换实现，不替代真实世界、物品、命令、皮肤、权限和模组包的
实机验收。

## 4. 隔离验证

先在 owl5 建立仅监听回环地址的临时 Velocity 实例，不占用生产端口：

1. 复制生产 Velocity 核心和最小配置到独立目录，保留生产目录只读。
2. 临时代理加载 HubCommand、ViaVersion、ViaBackwards 与独立 Authorizer；只使用
   隔离 API 生成的短时凭据，不读取生产授权凭据。
3. 通过 SSH 本地端口转发连接临时代理，不开放新的公网端口。
4. 使用正确的恐怖整蛊 1.20.1 档案进入恐怖整蛊服，再执行 `/hub` 进入 1.21.11 大厅。
5. 核对 UUID、皮肤、权限、物品栏、聊天、命令树、移动和正常退出。
6. 重复进入恐怖整蛊、返回大厅和再次进入，至少保留两次成功样本。
7. 停止临时代理并删除临时监听；确认生产 Velocity、游戏服进程和配置未变化。

任何解码错误、物品错位、命令异常、身份变化或后端断线都算失败。

隔离代理使用
[`Manage-PvpReturnStaging.ps1`](../deploy/windows/velocity-staging/Manage-PvpReturnStaging.ps1)
管理：

```powershell
.\Manage-PvpReturnStaging.ps1 -Action Prepare
.\Manage-PvpReturnStaging.ps1 -Action Start
.\Manage-PvpReturnStaging.ps1 -Action Status
.\Manage-PvpReturnStaging.ps1 -Action Stop
.\Manage-PvpReturnStaging.ps1 -Action Remove -ConfirmRemoval
```

`Prepare` 只注册无自动触发器的任务，不会启动代理；`Start` 会在监听前后复核生产配置
哈希、生产 Via 禁用状态、制品哈希、回环绑定和启动日志。`Remove` 必须显式传入
`-ConfirmRemoval`，且只允许删除核对后的隔离根目录。

API 候选与授权凭据分别由以下脚本管理：

```text
deploy/linux/manage-protocol-translation-staging.sh
deploy/windows/velocity-staging/Set-PvpReturnStagingAuthorization.ps1
deploy/windows/velocity-staging/Install-PvpReturnStagingCredential.ps1
```

API 管理器把生产备份恢复到独立数据库，只监听 `127.0.0.1:18093`；`issue-grant`
只签发隔离数据库中的短时授权。凭据安装器通过两个 SSH 进程的标准流复制 64 字节
原始数据，不写本机磁盘、不放进命令参数，也不输出凭据。Velocity 配置和同前缀历史
备份的 ACL 会从零重建，只允许 `SYSTEM`、本机 `Administrators` 与本机
`Administrator` 完全控制。

2026-07-28 已在 `E:\Velocity-PvpReturn-Staging` 启动只监听
`127.0.0.1:25579` 的隔离代理。ViaVersion/ViaBackwards `5.11.0` 与
HubCommand、Authorizer `0.3.1` 候选均正常加载，错误日志为 `0`。`0.3.1` 会在
每次实际放行后更新会话来源，避免恐怖整蛊 -> Lobby 后仍以恐怖整蛊作为下一次兼容判定来源；
生产 Authorizer 仍为 `0.3.0`。隔离 Authorizer
保持 `monitor`，凭据认证探针返回预期的 `PlayerNotLinked`。通过 SSH 本地转发执行
[`Test-MinecraftProtocolStatus.ps1`](../tools/acceptance/Test-MinecraftProtocolStatus.ps1)，
协议 `393`、`763` 和 `774` 分别协商为相同协议号。它证明最低支持边界、恐怖整蛊版本和
大厅版本的 Via 状态链可用，不替代真实正版登录与后端转服。机器可读证据见
[`PVP_RETURN_PROTOCOL_STAGING_2026-07-28.json`](evidence/PVP_RETURN_PROTOCOL_STAGING_2026-07-28.json)。
候选修复与隔离部署见
[`VELOCITY_AUTHORIZER_RELEASE_0.3.1_CANDIDATE.md`](VELOCITY_AUTHORIZER_RELEASE_0.3.1_CANDIDATE.md)。

API `0.21.0` 候选随后使用生产备份恢复独立临时 PostgreSQL 数据库，只监听
`127.0.0.1:18093` 完成授权验收。迁移 018 将全部既有目标初始化为关闭；恐怖整蛊到大厅
在关闭时返回 `MinecraftVersionMismatch`，只为大厅开启后返回 `Allowed`，大厅到
恐怖整蛊仍拒绝。即使临时为 Activity 开启协议转换，恐怖整蛊档案仍被
`ClientProfileMismatch` 拒绝。开关重置后再次恢复版本拒绝。生产 API、数据库、
Via JAR 和目录均未变化。初次自动验收资源已经删除；为真实会话准备的同构隔离副本
随后重新创建并保持回环监听，测试完成后必须执行 `stop` 和显式确认的 `remove`。

真实会话副本不会自行接收生产状态采集器的心跳。验收前执行
`manage-protocol-translation-staging.sh refresh-heartbeats`，只读复制生产数据库中
最新的 `lobby` 与历史 ID `pvp` 两条心跳到隔离数据库；两条记录必须同时存在、
60 秒内新鲜且在线，否则命令失败关闭。这里的 `pvp` 只表示
`C:\mc\server` 的恐怖整蛊服，不表示已停机的真正 PVP
`E:\MinecraftServer`。2026-07-28 实测复制后候选目录分别返回
`大厅 / Online / 1.21.11 / Paper` 与
`恐怖整蛊 / Online / 1.20.1 / Fabric`，Activity 的过期副本心跳仍保持
`Closed`。该动作不更新生产表、不启动或重启任何游戏服。
证据见
[`API_PROTOCOL_TRANSLATION_CANDIDATE_2026-07-28.json`](evidence/API_PROTOCOL_TRANSLATION_CANDIDATE_2026-07-28.json)。

## 5. 生产启用顺序

隔离验证通过后才按以下顺序执行：

1. 发布包含迁移 018 的 API；迁移默认关闭，不改变现网行为。
2. 在后台确认所有服务器的协议转换开关均为关闭。
3. 备份 Velocity 插件目录、配置、计划任务状态和最新日志。
4. 在维护窗口启用 ViaVersion/ViaBackwards 并重启 Velocity，先验证同版本
   Lobby、Survival1、Survival2、Activity 和恐怖整蛊首次路由均无回归。
5. 只为 `lobby` 开启协议转换并确认审计记录。
6. 使用恐怖整蛊档案真实执行 `/hub`、再次进入恐怖整蛊、断线重连和正常退出。
7. 保持 Velocity 授权为 `monitor`，直到四级账号和全部转服路径通过。

## 6. 回滚

出现问题时先关闭 `lobby` 的协议转换开关，使跨版本路径立即恢复硬拒绝；随后在维护
窗口停代理、恢复已备份插件目录并重启。回滚不修改游戏服、不改 modern forwarding
密钥，也不改变其他服务器目录或访问等级。

## 7. 完成标准

只有隔离代理、生产同版本回归、恐怖整蛊 `/hub`、成功重连、身份/皮肤/权限和日志脱敏
全部通过后，才把“恐怖整蛊返回大厅”从待验收改为已完成。

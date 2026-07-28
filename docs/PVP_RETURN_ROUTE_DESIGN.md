# PVP 跨版本返回大厅设计

> 状态：代码已实现、默认关闭；回环隔离代理的 1.20.1/1.21.11 status 协议协商已通过，
> 真实登录、`/hub` 和生产灰度仍待完成。
>
> 生产 Velocity 的 ViaVersion/ViaBackwards 仍为 `.disabled`，大厅目录开关仍为关闭，
> 本阶段没有重启代理或游戏服。

## 1. 问题

PVP 使用 Minecraft `1.20.1` Fabric 客户端，大厅使用 Minecraft `1.21.11`
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

生产目标只计划对 `lobby` 开启。这样 PVP 可以返回大厅，但大厅不能用基础客户端
跨版本进入 PVP，其他服务器也不会自动接受不同版本。

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
2. 临时代理只加载 HubCommand、ViaVersion 与 ViaBackwards，不加载生产授权凭据。
3. 通过 SSH 本地端口转发连接临时代理，不开放新的公网端口。
4. 使用正确 PVP 1.20.1 档案进入 PVP，再执行 `/hub` 进入 1.21.11 大厅。
5. 核对 UUID、皮肤、权限、物品栏、聊天、命令树、移动和正常退出。
6. 重复进入 PVP、返回大厅和再次进入，至少保留两次成功样本。
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

2026-07-28 已在 `E:\Velocity-PvpReturn-Staging` 启动只监听
`127.0.0.1:25579` 的隔离代理。ViaVersion/ViaBackwards `5.11.0` 与
HubCommand 均正常加载，错误日志为 `0`。通过 SSH 本地转发执行
[`Test-MinecraftProtocolStatus.ps1`](../tools/acceptance/Test-MinecraftProtocolStatus.ps1)，
协议 `763` 和 `774` 分别协商为 `763` 和 `774`。这证明 Via 协议链和状态握手可用，
不替代真实正版登录与后端转服。机器可读证据见
[`PVP_RETURN_PROTOCOL_STAGING_2026-07-28.json`](evidence/PVP_RETURN_PROTOCOL_STAGING_2026-07-28.json)。

## 5. 生产启用顺序

隔离验证通过后才按以下顺序执行：

1. 发布包含迁移 018 的 API；迁移默认关闭，不改变现网行为。
2. 在后台确认所有服务器的协议转换开关均为关闭。
3. 备份 Velocity 插件目录、配置、计划任务状态和最新日志。
4. 在维护窗口启用 ViaVersion/ViaBackwards 并重启 Velocity，先验证同版本
   Lobby、Survival1、Survival2、Activity 和 PVP 首次路由均无回归。
5. 只为 `lobby` 开启协议转换并确认审计记录。
6. 使用 PVP 档案真实执行 `/hub`、再次进入 PVP、断线重连和正常退出。
7. 保持 Velocity 授权为 `monitor`，直到四级账号和全部转服路径通过。

## 6. 回滚

出现问题时先关闭 `lobby` 的协议转换开关，使跨版本路径立即恢复硬拒绝；随后在维护
窗口停代理、恢复已备份插件目录并重启。回滚不修改游戏服、不改 modern forwarding
密钥，也不改变其他服务器目录或访问等级。

## 7. 完成标准

只有隔离代理、生产同版本回归、PVP `/hub`、成功重连、身份/皮肤/权限和日志脱敏
全部通过后，才把“PVP 返回大厅”从待验收改为已完成。

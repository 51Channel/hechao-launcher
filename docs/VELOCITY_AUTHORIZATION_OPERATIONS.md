# Velocity 进服授权运维

> 当前生产：API `0.22.0`、启动器 `0.12.1`、Velocity Authorizer `0.4.0`
> （`monitor`）、Lobby Guard `0.1.0`
> 当前状态：生产部署和自动验收完成，等待真实账号灰度后切换 `enforce`
>
> owl9 边界：Velocity 目标 `pvp` 当前只路由到恐怖整蛊服
> `C:\mc\server`；真正 PVP 服 `E:\MinecraftServer` 尚无独立目标，不得通过该
> 历史别名操作或验收。

## 1. 授权链路

```text
赫朝启动器完成 Microsoft / Minecraft 正版登录
  -> 玩家点击一个有权限且在线的服务器
  -> 启动器在创建 Minecraft 进程前申请 10 分钟一次性启动授权
  -> Minecraft 连接 online-mode + modern forwarding 的 Velocity
  -> Velocity 在 ServerPreConnectEvent 中异步请求赫朝 API
  -> 首次连接消费启动授权，并把初始大厅目标改写为授权选择的后端目标
  -> 玩家直接进入授权的后端服，不会完成大厅登录
  -> 更换服务器时由启动器先退出当前 Minecraft，再为新目标申请授权并启动正确档案
```

启动授权证明本次 Minecraft 连接由已登录的赫朝启动器发起，不是可以交给游戏客户端使用的票据。授权 ID 不写入 Minecraft 参数，Velocity 根据正版 UUID 向 API 消费最新、未使用且未过期的授权。

首次连接时，启动授权中的服务器选择是唯一权威目标。启动器始终连接统一 Velocity
公网入口；`lobby` 只作为 Velocity 触发首次 `ServerPreConnectEvent` 的内部占位，
Authorizer `0.4.0` 必须在玩家完成后端登录前把目标改写到授权后端。无授权、API
不可用、配置损坏、未知目标、基础设施目标或目标改写失败时，所有运行模式都必须硬拒绝，
不得让玩家落入大厅或其他默认目标。

插件把首次授权返回的服务器 ID 保存为 `sessionServerId`。它代表启动当前
Minecraft 进程时所选客户端档案。当前架构不提供游戏内转服；`/hub`、`/lobby`、
NPC、代理 fallback 和后端转服插件均不能成为玩家换服入口。若有旧插件仍发起后续
转服，Authorizer 只允许满足权限与档案兼容的玩家后端目标，并永久拒绝 `lobby`：

- Minecraft 版本不同：`MinecraftVersionMismatch`。
- Forge、Fabric 或 NeoForge 目标的客户端档案 ID 不同：
  `ClientProfileMismatch`。
- 同版本 Paper/Vanilla 目标：允许互转。
- 基础设施目标或 Lobby：`InfrastructureTargetDenied`。

启动器只在 Java、依赖库、游戏参数和进程对象均准备完成后申请授权。API 请求失败时会释放未启动的进程对象，不会留下一个随后必然被拒绝的 Minecraft 进程。

## 2. 最终判定顺序

API 每次按以下顺序判定：

1. Minecraft UUID 必须已绑定赫朝用户。
2. 用户不能处于停用状态。
3. Velocity 目标必须映射到一个 `Player` 角色的平台服务器；基础设施角色永久拒绝。
4. 服务器状态必须是 `Online`。
5. 有效的单服 `Deny` 优先于等级。
6. 有效的单服 `Allow` 可以越过等级和快照新鲜度，但不能越过账号停用或服务器关闭。
7. 高于 `Member` 的服务器要求 LuckPerms 快照不超过 20 分钟。
8. 玩家等级必须不低于服务器最低等级。
9. 首次连接还必须有未消费、未撤销、未过期的一次性启动授权。
10. 后续连接必须与本次启动会话的 Minecraft 版本和目标模组档案兼容。

授权默认有效 10 分钟。每个用户新建授权时会撤销其仍未消费的旧授权；成功的首次连接只能消费一次。授权创建、消费和拒绝均写入 `launcher.audit_logs`。

## 3. 组件与秘密边界

API 端点：

```text
POST /v1/velocity/launch-grants
POST /v1/internal/velocity/authorize
```

第一个端点要求玩家 Bearer 会话；第二个端点只接受 `X-Hechao-Velocity-Token` 内部凭据。API 环境文件只保存内部凭据的 SHA-256：

```text
VelocityAuthorization__InternalTokenSha256
VelocityAuthorization__LaunchGrantMinutes
VelocityAuthorization__MaximumLuckPermsAgeMinutes
VelocityAuthorization__RequireGrantIpMatch
```

游戏 VPS 上的配置位于：

```text
E:\Velocity\plugins\hechao-velocity-authorizer\config.properties
```

Velocity 发请求时必须持有凭据明文，因此该文件 ACL 只允许 `SYSTEM`、本机 `Administrators` 和当前服务器管理员。凭据不得写入 Git、文档、日志、命令历史或聊天记录。当前未强制授权来源 IP 一致，避免玩家 NAT、IPv4/IPv6 切换或运营商出口变化造成误拒绝；来源 IP 仍会进入审计记录。

## 4. 三种运行模式

| 模式 | API 允许 | API 拒绝 | API 不可用或配置错误 |
| --- | --- | --- | --- |
| `disabled` | 首次连接拒绝 | 首次连接拒绝 | 首次连接拒绝 |
| `monitor` | 放行 | 首次连接拒绝；普通玩家后端之间的后续权限拒绝只记录 | 首次连接拒绝 |
| `enforce` | 放行 | 拒绝 | 拒绝，故障关闭 |

`monitor` 只用于观察已经建立授权会话后的普通玩家后端判定，不是首次入口的放行开关。
从 `0.4.0` 起，首次连接在插件未初始化、模式禁用、配置解析失败、API 超时或任何响应
不完整时一律故障关闭。客户端不兼容、未知目标和基础设施目标也不受 monitor 放行影响。

## 5. 当前生产基线

- 当前生产插件：`E:\Velocity\plugins\HechaoVelocityAuthorizer-0.4.0.jar`
- JAR 大小：`22,967` 字节
- JAR SHA-256：`D3CEB0624A0AD70045897521795F275BC61973CF119873114149BDAEEAA95120`
- 部署备份：`E:\manual-backups\VelocityAuthorizer-0.4.0-20260729T150949Z`
- 旧回程备份：`E:\manual-backups\LegacyLobbyRouting-20260729T151223Z`
- API：`https://launcher-api.hechao.world/v1/internal/velocity/authorize`
- 代理实例：`owl5-main`
- 请求超时：`2500 ms`
- 当前配置模式：`monitor`
- 当前计划任务：`Codex-Velocity-Live`
- 代理监听：`25577`

生产 API 已使用匿名化的真实已绑定身份完成 `8/8` 客户端兼容矩阵：同版本 Paper
互转允许，Lobby 基础档案转 Activity 被 `ClientProfileMismatch` 拒绝，跨
1.21.11/1.20.1 被 `MinecraftVersionMismatch` 拒绝，Activity/恐怖整蛊原档案自连允许。
Authorizer `0.4.0` 的首次故障关闭、基础设施目标拒绝和普通后端会话兼容行为由
`26/26` 个 Java 测试覆盖。自动验收不替代真实玩家直接路由、切服、断线重连和
API 故障演练。正式记录见
[`VELOCITY_AUTHORIZER_RELEASE_0.4.0.md`](VELOCITY_AUTHORIZER_RELEASE_0.4.0.md)。

owl9 的恐怖整蛊 Fabric `1.20.1` 后端已安装 FabricProxy-Lite `2.6.0`，保持
`online-mode=true` 并使用与代理一致的 modern forwarding 密钥。真实会话已完成
统一入口、身份一致、后端直连拒绝、稳定连接和正常退出验收。真正 PVP
`E:\MinecraftServer` 未被该历史目标操作。详细步骤见
[`PVP_VELOCITY_OPERATIONS.md`](PVP_VELOCITY_OPERATIONS.md)。

## 6. 从 monitor 切换到 enforce

以下条件必须全部完成：

1. [已完成] Minecraft Java API 许可已批准，正确赫朝客户端已完成真实 Microsoft
   正版登录与基础档案进服。
2. 普通、VIP、管理员、服主各至少一个账号完成目录和进服验收。当前 22 个社区账号中只有 1 个绑定 Minecraft，因此此项未完成。
3. [已完成] 管理员单独重启 Velocity，并从启动日志确认插件以 `monitor` 初始化。
4. [已完成] Velocity 的 `lobby`、`survival1`、`survival2`、`activity`、`pvp` 与 DollNight 对应目录都已登记；`lobby` 只保留内部占位，替换服共享目标关系已记录。
5. 共享同一 Velocity 目标的替换服一次只能有一个目录项处于 `Online`。特别是 `survival2` 与 DollNight 的切换必须先更新目录状态。
6. [部分完成] 生产兼容矩阵 `8/8` 和恐怖整蛊 modern forwarding 均已完成；
   仍需用正确档案验证统一入口、直接目标路由、身份转发、后端直连拒绝、启动器切服、
   断线重连、API 短暂失败和 Lobby 永久拒绝。
7. [已完成] 数据库已有可验证备份，API 和插件配置都有回滚副本。

随后由管理员安排一次 Velocity 手动重启窗口：

1. 把 `config.properties` 的 `mode` 改为 `enforce`。
2. 管理员手动重启 Velocity。
3. 验证没有启动器授权时被拒绝，有授权时只进入授权目标服，任何情况下都不能进入大厅。
4. 验证低等级、单服拒绝、维护服、未知目标和过期授权均被拒绝。
5. 最后把 API 的 `Authentication__EnforceCatalogAuthentication` 改为 `true` 并仅重启 API。

不得把“插件 JAR 已放入目录”误记为“强制授权已上线”。

## 7. 检查命令

游戏 VPS：

```powershell
Get-FileHash 'E:\Velocity\plugins\HechaoVelocityAuthorizer-0.4.0.jar' -Algorithm SHA256
Select-String -Path 'E:\Velocity\plugins\hechao-velocity-authorizer\config.properties' -Pattern '^mode='
Get-ChildItem 'E:\Velocity\logs' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 |
    Get-Content -Tail 300 |
    Select-String 'Hechao authorization|\[monitor\]|Denied '
```

API 主机：

```bash
systemctl status hechao-launcher-api.service --no-pager
curl -fsS http://127.0.0.1:8090/readyz
journalctl -u hechao-launcher-api.service -p warning --since today --no-pager
```

数据库目标核对：

```sql
SELECT id, velocity_target, status, minimum_tier, is_visible, server_role, monitoring_enabled
FROM launcher.servers
ORDER BY velocity_target, sort_order, id;
```

不带内部凭据和使用错误凭据请求内部端点都必须返回 `401`。不要把真实凭据直接写在可回显的命令行中。

## 8. 回滚

若 `monitor` 产生异常，可回退到上一版 Authorizer 并保留日志和审计记录；不能依靠
`disabled` 恢复玩家入口，因为 `0.4.0` 的首次连接在 disabled 下也会拒绝。若
`enforce` 阻断正常玩家，优先回退到 `monitor`，再检查目标映射、LuckPerms 新鲜度、
账号绑定和 API 可用性。任何回滚都必须保持 Lobby Guard、回环监听、空白名单与 API
基础设施角色，确保回滚不会重新开放大厅。

部署脚本会把旧 JAR、配置和 `velocity.toml` 备份到带时间戳的
`E:\manual-backups` 子目录。回滚时先恢复旧 JAR 与配置，再只重启 Velocity；若
API `0.22.0` 已迁移，不应仅为插件回滚而降级数据库。不要通过关闭数据库、停止大厅
或重启全部 Minecraft 服务来处理授权问题。

详细发布证据见
[`VELOCITY_AUTHORIZER_RELEASE_0.4.0.md`](VELOCITY_AUTHORIZER_RELEASE_0.4.0.md)。

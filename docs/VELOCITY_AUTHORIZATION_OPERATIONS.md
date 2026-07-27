# Velocity 进服授权运维

> API：`0.20.2`（目标定向契约自 `0.12.0` 起保持兼容）
> 启动器：`0.11.14` 私有 OSS 灰度候选
> Velocity 插件：`0.3.0`
> 当前状态：插件以 `monitor` 运行；版本/档案不兼容会立即拒绝，权限判定仍等待真实四级账号灰度

## 1. 授权链路

```text
赫朝启动器完成 Microsoft / Minecraft 正版登录
  -> 玩家点击一个有权限且在线的服务器
  -> 启动器在创建 Minecraft 进程前申请 10 分钟一次性启动授权
  -> Minecraft 连接 online-mode + modern forwarding 的 Velocity
  -> Velocity 在 ServerPreConnectEvent 中异步请求赫朝 API
  -> 首次连接消费启动授权，并把初始大厅目标改写为授权选择的后端目标
  -> Velocity 缓存本次进程最初使用的服务器/客户端档案
  -> 后续 NPC、命令或插件转服按实际目标重新校验权限与客户端兼容性
```

启动授权证明本次 Minecraft 连接由已登录的赫朝启动器发起，不是可以交给游戏客户端使用的票据。授权 ID 不写入 Minecraft 参数，Velocity 根据正版 UUID 向 API 消费最新、未使用且未过期的授权。

首次连接时，启动授权中的服务器选择是权威目标。启动器始终连接统一 Velocity 公网入口，即使代理先把玩家放到 `lobby`，API 也会返回授权对应的 `velocityTarget`，插件 `0.3.0` 再把本次 `ServerPreConnectEvent` 的目标改写到该后端。返回目标必须存在于当前 Velocity 注册表中；未知目标在 `monitor` 中只记录告警，在 `enforce` 中拒绝。

插件把首次授权返回的服务器 ID 保存为 `sessionServerId`。它代表启动当前
Minecraft 进程时所选客户端档案，不会在玩家经过大厅时改写。后续转服时，API
比较该来源与目标服：

- Minecraft 版本不同：`MinecraftVersionMismatch`。
- Forge、Fabric 或 NeoForge 目标的客户端档案 ID 不同：
  `ClientProfileMismatch`。
- 同版本 Paper/Vanilla 目标：允许互转。
- 模组客户端返回同版本 Paper 大厅：允许；再次进入原模组服仍使用最初档案判定。

启动器只在 Java、依赖库、游戏参数和进程对象均准备完成后申请授权。API 请求失败时会释放未启动的进程对象，不会留下一个随后必然被拒绝的 Minecraft 进程。

## 2. 最终判定顺序

API 每次按以下顺序判定：

1. Minecraft UUID 必须已绑定赫朝用户。
2. 用户不能处于停用状态。
3. Velocity 目标必须映射到一个可见的平台服务器。
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
| `disabled` | 不请求 | 不请求 | 放行 |
| `monitor` | 放行 | 权限类拒绝放行并记录；客户端版本/档案不兼容立即拒绝 | 放行并告警 |
| `enforce` | 放行 | 拒绝 | 拒绝，故障关闭 |

`monitor` 用于观察真实玩家、代理目标和目录映射，不是最终权限安全状态。客户端不兼容
属于会导致协议错误或崩溃的技术边界，因此从 `0.3.0` 起不受 monitor 放行影响。
配置解析失败时，插件会尽量从文件读取模式提示；已明确写成 `enforce` 的有效模式提示
会继续故障关闭。

## 5. 当前安装基线

- 插件：`E:\Velocity\plugins\HechaoVelocityAuthorizer-0.3.0.jar`
- JAR 大小：`21,152` 字节
- JAR SHA-256：`289B13472AEAC4073895EF9BE7E630B4B5AACEC48A4D0FD849BBAFE0064E681D`
- API：`https://launcher-api.hechao.world/v1/internal/velocity/authorize`
- 代理实例：`owl5-main`
- 请求超时：`2500 ms`
- 安装备份：`E:\manual-backups\VelocityAuthorizer-0.3.0-20260727T231243Z`
- 当前配置模式：`monitor`
- 当前计划任务：`Codex-Velocity-Live`
- 当前监听：`[::]:25577`，PID `472`

2026-07-28 部署 `0.3.0` 前确认代理没有已建立的玩家连接，只重启计划任务
`Codex-Velocity-Live`。启动日志确认插件以 `monitor` 为 `owl5-main` 初始化，代理
监听 `25577`，公网 `mc.hehe11.fun:15156` TCP 可达。大厅、Survival1、
Survival2 和活动服 PID 保持 `5300`、`5540`、`9428`、`6112`，均未重启。

生产 API 已使用匿名化的真实已绑定身份完成 `8/8` 客户端兼容矩阵：同版本 Paper
互转允许，Lobby 基础档案转 Activity 被 `ClientProfileMismatch` 拒绝，跨
1.21.11/1.20.1 被 `MinecraftVersionMismatch` 拒绝，Activity/PVP 原档案自连允许。
插件目标改写和硬拒绝行为由 `13/13` 个 Java 测试覆盖。该自动验收不替代真实玩家
连接、NPC 转服和 `/hub` 灰度。

owl9 的 PVP Fabric `1.20.1` 后端已安装 FabricProxy-Lite `2.6.0`，保持
`online-mode=true` 并使用与代理一致的 modern forwarding 密钥。部署前后 PVP
Java 进程与内部 `25565` 监听均为空，Velocity PID 和任务定义也未改变。该结果只证明
静态兼容与密钥边界正确，仍需服主手动开服验证真实代理路由、身份数据和直连拒绝。
详细步骤见 [`PVP_VELOCITY_OPERATIONS.md`](PVP_VELOCITY_OPERATIONS.md)。

## 6. 从 monitor 切换到 enforce

以下条件必须全部完成：

1. [已完成] Minecraft Java API 许可已批准，正确赫朝客户端已完成真实 Microsoft
   正版登录与基础档案进服。
2. 普通、VIP、管理员、服主各至少一个账号完成目录和进服验收。当前 22 个社区账号中只有 1 个绑定 Minecraft，因此此项未完成。
3. [已完成] 管理员单独重启 Velocity，并从启动日志确认插件以 `monitor` 初始化。
4. [已完成] Velocity 的 `lobby`、`survival1`、`survival2`、`activity`、`pvp` 与 DollNight 对应目录都已登记；替换服共享目标关系已记录。
5. 共享同一 Velocity 目标的替换服一次只能有一个目录项处于 `Online`。特别是 `survival2` 与 DollNight 的切换必须先更新目录状态。
6. [部分完成] 生产兼容矩阵 `8/8`、基础档案 Lobby/Survival1/Survival2 真实转服、
   PVP modern forwarding 静态部署均已完成；仍需使用正确 Activity/PVP 档案验证
   统一入口、身份转发、直连拒绝、NPC 转服、`/hub`、断线重连和 API 短暂失败。
7. [已完成] 数据库已有可验证备份，API 和插件配置都有回滚副本。

随后由管理员安排一次 Velocity 手动重启窗口：

1. 把 `config.properties` 的 `mode` 改为 `enforce`。
2. 管理员手动重启 Velocity。
3. 验证没有启动器授权时被拒绝，有授权时大厅和允许的目标服可进入。
4. 验证低等级、单服拒绝、维护服、未知目标和过期授权均被拒绝。
5. 最后把 API 的 `Authentication__EnforceCatalogAuthentication` 改为 `true` 并仅重启 API。

不得把“插件 JAR 已放入目录”误记为“强制授权已上线”。

## 7. 检查命令

游戏 VPS：

```powershell
Get-FileHash 'E:\Velocity\plugins\HechaoVelocityAuthorizer-0.3.0.jar' -Algorithm SHA256
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
SELECT id, velocity_target, status, minimum_tier, is_visible
FROM launcher.servers
ORDER BY velocity_target, sort_order, id;
```

不带内部凭据和使用错误凭据请求内部端点都必须返回 `401`。不要把真实凭据直接写在可回显的命令行中。

## 8. 回滚

若 `monitor` 产生异常，只需将模式改为 `disabled`，由管理员在合适窗口手动重启 Velocity。若 `enforce` 阻断正常玩家，优先回退到 `monitor`，保留日志和审计记录，再检查目标映射、LuckPerms 新鲜度、账号绑定和 API 可用性。

部署脚本把旧 JAR、配置和 `velocity.toml` 备份到
`E:\manual-backups\VelocityAuthorizer-0.3.0-20260727T231243Z`。当前直接回滚副本为
`0.2.0`，SHA-256
`9CBBB1453D7260CD8AAD48EDC6BE4E80B8A5E41374D5012E0DBA64ACC0188D37`。
回滚时先恢复旧 JAR 与配置，再只重启 Velocity，并把 API 同步回滚到 `0.20.1`。
不要通过关闭数据库、停止大厅或重启全部 Minecraft 服务来处理授权问题。

详细发布证据见
[`VELOCITY_AUTHORIZER_RELEASE_0.3.0.md`](VELOCITY_AUTHORIZER_RELEASE_0.3.0.md)。

# Velocity 进服授权运维

> API：`0.5.0`
> 启动器：`0.6.0`
> Velocity 插件：`0.1.0`
> 当前状态：插件已安装为 `monitor`，等待管理员下一次手动重启 Velocity 后加载

## 1. 授权链路

```text
赫朝启动器完成 Microsoft / Minecraft 正版登录
  -> 玩家点击一个有权限且在线的服务器
  -> 启动器在创建 Minecraft 进程前申请 10 分钟一次性启动授权
  -> Minecraft 连接 online-mode + modern forwarding 的 Velocity
  -> Velocity 在 ServerPreConnectEvent 中异步请求赫朝 API
  -> 首次连接消费启动授权；后续转服重新校验目标服权限
```

启动授权证明本次 Minecraft 连接由已登录的赫朝启动器发起，不是可以交给游戏客户端使用的票据。授权 ID 不写入 Minecraft 参数，Velocity 根据正版 UUID 向 API 消费最新、未使用且未过期的授权。

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
| `monitor` | 放行 | 放行并记录 `Would deny` | 放行并告警 |
| `enforce` | 放行 | 拒绝 | 拒绝，故障关闭 |

`monitor` 用于观察真实玩家、代理目标和目录映射，不是最终安全状态。配置解析失败时，插件会尽量从文件读取模式提示；已明确写成 `enforce` 的有效模式提示会继续故障关闭。

## 5. 当前安装基线

- 插件：`E:\Velocity\plugins\HechaoVelocityAuthorizer-0.1.0.jar`
- JAR SHA-256：`BA1E02150714A34D5FCEA348C64C578B31D9E4C85B53D3DA8EFD3681F31388C4`
- API：`https://launcher-api.hechao.world/v1/internal/velocity/authorize`
- 代理实例：`owl5-main`
- 请求超时：`2500 ms`
- 安装备份：`E:\manual-backups\velocity-authorizer-20260723T103346Z`
- 当前配置模式：`monitor`

部署时 Velocity 的 `25577` 监听 PID 在前后均未变化。插件文件只会在管理员下一次手动重启 Velocity 后加载；本项目脚本不会自动重启代理或任何 Minecraft 后端服。

## 6. 从 monitor 切换到 enforce

以下条件必须全部完成：

1. Minecraft Java API 许可已批准，真实 Microsoft 正版登录成功。
2. 普通、VIP、管理员、服主各至少一个账号完成目录和进服验收。
3. 管理员手动重启 Velocity，并从启动日志确认插件以 `monitor` 初始化。
4. Velocity 的 `lobby`、`survival1`、`survival2`、`activity` 等所有可达目标都已登记到平台目录；历史目标如 `pvp` 要么登记，要么从代理配置和入口中移除。
5. 共享同一 Velocity 目标的替换服一次只能有一个目录项处于 `Online`。特别是 `survival2` 与 DollNight 的切换必须先更新目录状态。
6. `monitor` 日志中普通入口、NPC 转服、`/hub`、断线重连和 API 短暂失败均符合预期。
7. 数据库已有可验证备份，API 和插件配置都有回滚副本。

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
Get-FileHash 'E:\Velocity\plugins\HechaoVelocityAuthorizer-0.1.0.jar' -Algorithm SHA256
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

插件安装脚本会把旧 JAR 和配置备份到 `E:\manual-backups`，但不会执行重启。API `0.6.0` 可以继续在线，即使插件暂时禁用；不要通过关闭数据库、停止大厅或重启全部 Minecraft 服务来处理授权问题。

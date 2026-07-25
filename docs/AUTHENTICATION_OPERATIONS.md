# Microsoft 正版登录与 LuckPerms 权限

> 当前启动器源码版本：`0.11.4`
> 当前 API 源码与生产版本：`0.11.1`
> 当前生产状态：统一账号已部署；启动器 `0.11.4` 为内部候选；Velocity 仍为 `monitor`，目录强制登录尚未启用

## 1. 身份与权限边界

赫朝账号是启动器与 `hechao.world` 共用的社区身份，Microsoft/Minecraft Java 正版身份是可独立绑定的游戏所有权证明。启动器不采集 Microsoft 密码；赫朝账号密码只通过 TLS 发送，服务端使用 ASP.NET Core Identity PBKDF2 哈希保存，客户端不落盘保存密码。旧论坛 scrypt 哈希仅用于一次兼容登录，成功后立即升级。

登录链路：

```text
邮箱验证后注册赫朝统一账号，或使用账号名/邮箱登录
  -> 赫朝短期访问令牌 + 可轮换刷新令牌
  -> Windows 系统浏览器
  -> Microsoft OAuth 授权码 + PKCE
  -> Xbox User Token
  -> XSTS Token
  -> Minecraft Access Token
  -> 赫朝 API 校验 Java 权益与 Minecraft 档案并绑定身份
  -> 按 LuckPerms 主组过滤服务器目录
```

启动器 `0.11.4` 继续使用系统浏览器、授权码和 PKCE，不改用设备码，也不接收 Microsoft 密码。绑定期间启动器显示可取消的中文等待层；回环地址完成页由 MSAL 返回赫朝样式的中文成功或重试页面。取消操作会终止本次授权请求，错误密码、浏览器取消、MSAL 缓存异常和网络故障只更新界面状态，不得退出进程。

玩家等级的权威来源是游戏 VPS 上共享 MariaDB 中的 LuckPerms 数据。当前映射为：

| LuckPerms 主组 | 启动器等级 |
| --- | --- |
| `default` | `Member` |
| `vip` | `Participant` |
| `admin` | `Collaborator` |
| `owner` | `Administrator` |

未知组按 `Member` 处理。客户端过滤只用于界面，最终进服授权仍必须由 Velocity 或后端插件再次校验。

## 2. 已实现组件与生产状态

- API 玩家端点：`POST /v1/auth/login`、`POST /v1/auth/minecraft/link`、`POST /v1/auth/refresh`、`POST /v1/auth/logout`、`GET /v1/me`。旧 `POST /v1/auth/register` 从 `0.11.0` 起返回升级提示，避免绕过社区邮箱验证生成不同步账号。
- 新账号统一通过 `hechao.world` 邮箱验证码注册；启动器 `0.11.0` 起直接调用同一注册接口，完成后自动登录。
- 论坛内部桥接只接受本机回环来源与独立高熵令牌；注册、登录、旧账号导入、改密、重置密码和显示名称同步均走内部端点。
- 论坛密码变更会同时撤销启动器认证状态和其他论坛设备会话。完整边界与部署顺序见 [`UNIFIED_ACCOUNT_OPERATIONS.md`](UNIFIED_ACCOUNT_OPERATIONS.md)。
- API `0.10.1` 已上线 `POST /v1/auth/logout-all` 和 `POST /v1/auth/minecraft/unlink`。
- `logout-all` 在一个数据库事务中撤销该账号的启动器会话、管理员浏览器会话、未使用后台登录票据和未使用 Velocity 进服授权；成功后客户端同时清除本机 DPAPI 会话与 Microsoft 缓存。
- `minecraft/unlink` 必须再次提交当前赫朝账号密码。校验成功后撤销上述全部认证状态、删除 Minecraft 身份绑定并将启动器等级回退为 `Member`；密码错误、账号不存在或当前未绑定均不会部分执行。
- 旧 `POST /v1/auth/minecraft/exchange` 暂时保留迁移兼容。
- 旧版 `minecraft/exchange` 暂时保留兼容；其临时 `legacy_*` 账户在绑定同一正版身份时可安全转入正式赫朝账号，正式账户之间不能互相接管。
- 进服端点：`POST /v1/velocity/launch-grants` 和内部 `POST /v1/internal/velocity/authorize`。
- 访问令牌默认有效 15 分钟；刷新令牌默认有效 30 天并在每次刷新时轮换。
- PostgreSQL 只保存访问令牌与刷新令牌的 SHA-256，不保存令牌明文。
- Windows 客户端使用 DPAPI 保护赫朝刷新会话，MSAL 缓存也使用 Windows 安全存储。
- LuckPerms 同步任务：`Hechao Launcher LuckPerms Sync`，以 `SYSTEM` 身份每 5 分钟只读同步。
- 同步目录：`C:\ProgramData\Hechao\LauncherBridge`。
- 同步凭据使用 DPAPI LocalMachine 加密，ACL 只允许 `SYSTEM` 与本机管理员。
- API 内部同步端点只接受独立高熵凭据；阿里云环境文件只保存其 SHA-256。
- Velocity 插件只异步调用 HTTPS API，不读取 LuckPerms 数据库；其内部凭据与 LuckPerms 同步凭据相互独立。
- Velocity 插件已安装为 `monitor`，部署过程未重启代理，将在管理员下一次手动重启后加载。

2026-07-22 的生产验收结果为 114 名玩家：`default=99`、`vip=12`、`admin=1`、`owner=2`。

2026-07-24 已部署 API `0.9.0-20260723T195253Z` 和迁移 7。生产隔离账号验证了注册、本人信息、目录、刷新轮换、刷新令牌重放拒绝、退出撤销、密码登录和无效 Minecraft 凭据拒绝；测试账号、会话与对应审计记录随后已清理。

同日先部署 `0.10.0-20260724T101528Z`，隔离回归在正确密码解除绑定时发现 Npgsql 不能把带参数的多条 SQL 放入同一个预处理命令。`0.10.1-20260724T102830Z` 将解除绑定、旧身份转移和身份更新事务拆为单语句命令，没有新增数据库迁移。

`0.10.1` 生产回归验证了三个启动器会话和一个管理员会话被 `logout-all` 同时撤销，未使用后台票据与 Velocity 进服授权一并失效；三个旧访问令牌均返回 401。重新登录后，错误密码解除绑定返回 403 且数据库状态完全不变；正确密码解除绑定返回 204，并撤销当前会话、后台会话、票据和进服授权，删除 Minecraft 身份，将等级从 `Participant` 回退为 `Member`。隔离测试账号、身份、会话和审计记录随后精确清理，生产用户数恢复为 `0`。该验证证明赫朝账号安全链路可用，不替代等待 Minecraft API 许可后的真实正版账号与四级 LuckPerms 验收。

2026-07-25 已部署 API `0.11.0-20260725T074100Z`、数据库迁移 8 和论坛统一账号改版，22 个论坛账号均已关联统一身份。启动器 `0.11.1` 修正登录接口把错误密码 401 误判为“会话过期”的问题，并为所有登录与安装入口增加可恢复的异步异常边界。

## 3. Microsoft 应用注册

赫朝自己的 Microsoft 公共客户端应用已于 2026-07-22 注册，不能借用其他启动器的 Client ID。

1. 在 Microsoft Entra 管理中心注册应用。
2. 支持的账户类型选择“个人 Microsoft 账户”。
3. 平台选择“移动和桌面应用程序”，重定向 URI 使用 `http://localhost`。
4. 使用明确登记的桌面回调和授权码 + PKCE；不要额外开启设备码或密码回退流，也不创建或打包客户端密码。
5. 客户端请求 `XboxLive.signin` 与 `XboxLive.offline_access`。
6. 向 Mojang/Minecraft 申请 Java Game Service API 访问许可；新第三方应用未获许可时会返回 `Invalid app registration`。
7. Client ID 已写入 `0.10.0` 客户端候选；环境变量 `HECHAO_MICROSOFT_CLIENT_ID` 仍可用于内部覆盖测试。

启动器和官网必须持续展示非官方产品声明，赫朝品牌保持主导，不得使用 Minecraft 官方徽标或暗示获得 Mojang/Microsoft 认可。客户端只分发自有模组、配置与资源；Minecraft 本体和官方资源必须通过合法官方服务获取。

当前已完成应用注册、个人 Microsoft 帐户范围和 `http://localhost` 桌面回调校验。Minecraft Java API 访问许可已于 2026-07-22 提交申请，当前等待审核；在许可通过前必须保持目录强制登录关闭。

2026-07-24 的本机链路诊断确认 Microsoft OAuth 与 MSAL 缓存正常。启动器 `0.7.0` 曾把 Xbox 请求字段错误序列化为小写驼峰，导致 Xbox User Token 接口返回 HTTP 400 空响应；`0.7.1` 已改为官方要求的字段大小写，并只向 Xbox User Token 与 XSTS 端点发送 `x-xbl-contract-version: 1`。修复后 Xbox User Token 与 XSTS 均返回 200，随后 Minecraft Game Service 明确返回 HTTP 403 `Invalid app registration`，与当前应用许可仍在审核中的状态一致。诊断过程不记录或输出访问令牌。

官方参考：

- [MSAL .NET 系统浏览器](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers)
- [Microsoft 身份平台应用流程](https://learn.microsoft.com/en-us/entra/identity-platform/authentication-flows-app-scenarios)
- [Xbox Live 网站身份验证](https://learn.microsoft.com/en-us/gaming/gdk/docs/services/fundamentals/s2s-auth-calls/service-authentication/live-website-authentication)
- [Minecraft API 应用访问申请](https://help.minecraft.net/hc/en-us/articles/16254801392141)

## 4. 强制登录启用顺序

生产环境当前保持：

```text
Authentication__EnforceCatalogAuthentication=false
```

这是有意的过渡状态。Client ID 或 Minecraft API 许可未完成时提前改成 `true`，会让所有玩家无法加载服务器目录。

启用顺序必须是：

1. 完成 Microsoft 应用注册和 Minecraft API 许可。
2. 用至少一个普通组、VIP、管理员和服主分别完成赫朝账号注册、登录、Microsoft 绑定和会话恢复测试。
3. 验证新账号与旧 `legacy_*` 身份接管、Minecraft UUID、LuckPerms 快照和目录过滤结果正确。
4. 由管理员手动重启 Velocity，以 `monitor` 模式加载最终授权插件。
5. 核对所有 Velocity 目标与平台目录映射，完成首次连接、NPC 转服、`/hub`、断线重连和 API 故障测试。
6. 在维护窗口把插件改为 `enforce` 并由管理员手动重启 Velocity。
7. 将 `Authentication__EnforceCatalogAuthentication` 改为 `true`，只重启启动器 API。
8. 验证匿名目录返回 401、有效账号只看到授权服务器、未使用启动器的连接被拒绝、旧网站与中转 API 保持正常。

在第 6 步完成前，“启动器强制登录”不能等同于“服务器最终权限防线”。Velocity 的正版验证与每个目标服的等级授权是两层不同检查。详细模式、目标映射和回滚步骤见 [`VELOCITY_AUTHORIZATION_OPERATIONS.md`](VELOCITY_AUTHORIZATION_OPERATIONS.md)。

## 5. 运维检查

Windows VPS：

```powershell
Get-ScheduledTask -TaskName 'Hechao Launcher LuckPerms Sync'
Get-ScheduledTaskInfo -TaskName 'Hechao Launcher LuckPerms Sync'
Get-Content 'C:\ProgramData\Hechao\LauncherBridge\sync.log' -Tail 20
```

阿里云 API：

```bash
curl -fsS http://127.0.0.1:8090/readyz
curl -i https://launcher-api.hechao.world/v1/me
journalctl -u hechao-launcher-api.service -p warning --since today --no-pager
```

日志不得输出 Microsoft、Xbox、Minecraft、赫朝会话或内部同步令牌。同步失败时先检查任务结果、HTTPS 与 MariaDB 只读查询，不要通过重启 Minecraft 服务端处理。

## 6. `0.10.1` 部署与回滚边界

- 部署前按 [`API_OPERATIONS.md`](API_OPERATIONS.md) 备份生产数据库、当前 API 二进制、环境文件和 `current` 链接；本版本不需要执行新迁移。
- 隔离账号已验证普通登录、`logout-all`、重新登录、错误密码解除拒绝和正确密码解除；真实 Microsoft/Minecraft 绑定仍需等待 Java API 许可。
- API 回滚可恢复到 `0.9.0-20260723T195253Z`，无需回滚数据库结构；已经撤销的会话、票据和进服授权不会恢复，已解除的 Minecraft 绑定也必须由玩家重新验证后绑定。
- 回滚或部署 API 不需要、也不得顺带启动、停止或重启任何 Minecraft 服务端。

# Microsoft 正版登录与 LuckPerms 权限

> 当前客户端源码版本：`0.7.0`
> 当前生产状态：认证 API、LuckPerms 同步和 Velocity 授权链已部署；Velocity 仍为 `monitor`，目录强制登录尚未启用

## 1. 身份与权限边界

赫朝启动器只接受 Microsoft 正版 Minecraft: Java Edition 身份，不建立独立密码体系，也不采集 Microsoft 密码。

登录链路：

```text
Windows 系统浏览器
  -> Microsoft OAuth 授权码 + PKCE
  -> Xbox User Token
  -> XSTS Token
  -> Minecraft Access Token
  -> 赫朝 API 校验 Java 权益与 Minecraft 档案
  -> 赫朝短期会话
  -> 按 LuckPerms 主组过滤服务器目录
```

玩家等级的权威来源是游戏 VPS 上共享 MariaDB 中的 LuckPerms 数据。当前映射为：

| LuckPerms 主组 | 启动器等级 |
| --- | --- |
| `default` | `Member` |
| `vip` | `Participant` |
| `admin` | `Collaborator` |
| `owner` | `Administrator` |

未知组按 `Member` 处理。客户端过滤只用于界面，最终进服授权仍必须由 Velocity 或后端插件再次校验。

## 2. 已部署组件

- API 端点：`POST /v1/auth/minecraft/exchange`、`POST /v1/auth/refresh`、`POST /v1/auth/logout`、`GET /v1/me`。
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

## 3. Microsoft 应用注册

赫朝自己的 Microsoft 公共客户端应用已于 2026-07-22 注册，不能借用其他启动器的 Client ID。

1. 在 Microsoft Entra 管理中心注册应用。
2. 支持的账户类型选择“个人 Microsoft 账户”。
3. 平台选择“移动和桌面应用程序”，重定向 URI 使用 `http://localhost`。
4. 使用明确登记的桌面回调和授权码 + PKCE；不要额外开启设备码或密码回退流，也不创建或打包客户端密码。
5. 客户端请求 `XboxLive.signin` 与 `XboxLive.offline_access`。
6. 向 Mojang/Minecraft 申请 Java Game Service API 访问许可；新第三方应用未获许可时会返回 `Invalid app registration`。
7. Client ID 已写入 `0.6.0` 客户端；环境变量 `HECHAO_MICROSOFT_CLIENT_ID` 仍可用于内部覆盖测试。

启动器和官网必须持续展示非官方产品声明，赫朝品牌保持主导，不得使用 Minecraft 官方徽标或暗示获得 Mojang/Microsoft 认可。客户端只分发自有模组、配置与资源；Minecraft 本体和官方资源必须通过合法官方服务获取。

当前已完成应用注册、个人 Microsoft 帐户范围和 `http://localhost` 桌面回调校验。Minecraft Java API 访问许可已于 2026-07-22 提交申请，当前等待审核；在许可通过前必须保持目录强制登录关闭。

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
2. 用至少一个普通组、VIP、管理员和服主账号完成真实登录测试。
3. 验证账号 Minecraft UUID 与 LuckPerms 快照一致，目录过滤结果正确。
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

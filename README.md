# 赫朝启动器

赫朝 Minecraft 社区的 Windows 桌面启动器。当前客户端源码版本是 `0.7.0`，API 源码版本是 `0.8.0`。平台已经完成视觉系统、HTTPS 服务器目录、Microsoft/Minecraft 正版认证链路、LuckPerms 等级同步、权限过滤、签名客户端分发、断点续传、SHA-256 校验、修复、原子版本切换、真实 Minecraft 启动、Velocity 服务端二次授权、只读实时状态采集，以及带独立浏览器会话和双重验证的管理员控制台。

由赫朝独立运营。非 Minecraft 官方产品。未经 Mojang 或 Microsoft 批准，也不与 Mojang 或 Microsoft 关联。

## 当前能力

- 展示大厅、生存服和活动服的状态、在线人数、核心与 Minecraft 版本。
- 切换服务器并根据在线/维护状态控制主操作按钮。
- 读取经 ECDSA P-256 签名的客户端清单；未知公钥、篡改负载、危险路径和远程明文 HTTP 会被拒绝。
- 使用 HTTP Range 断点续传和 SHA-256 逐文件校验，下载失败时保留 `.part` 供下次继续。
- 在独立暂存目录构建完整客户端，通过目录重命名切换活动版本，并保留一个 `.previous` 版本供回滚。
- 修复流程会重新检查本地文件；同档案的并发安装通过跨进程独占锁阻止。
- 提供通知、客户端修复入口和游戏内存设置。
- 将所选服务器和内存保存到 `%LocalAppData%\Hechao\Launcher\settings.json`。
- 通过 `IServerCatalogClient` 从 HTTPS API 读取服务器目录，并按“在线 API、上次成功缓存、内置应急目录”顺序降级。
- 使用系统浏览器执行 Microsoft OAuth 与 PKCE，再通过 Xbox/XSTS/Minecraft 验证 Java 正版权益。
- 使用 15 分钟访问令牌和可撤销、轮换的刷新令牌；刷新会话由 Windows DPAPI 保护。
- 从共享 LuckPerms 数据库每 5 分钟同步主组，按 `Member`、`Participant`、`Collaborator`、`Administrator` 过滤目录。
- 私有 OSS 下载通过启动器 API 鉴权；API 仅为清单内对象签发 5 分钟 V4 URL，Bearer 不会随跳转发送到 OSS。
- 生产发布公钥已内嵌，启动器只信任 `release-2026-07-primary`；私钥使用 Windows DPAPI 加密离线保存，不进入仓库或服务端。
- 使用受管 Java 21 运行时和签名档案中的 Fabric 客户端构建正版会话，直接连接 `mc.hehe11.fun`；真实进程构建冒烟测试已通过，测试过程未启动游戏。
- 在 Minecraft 进程启动前申请 10 分钟、一次性 Velocity 启动授权；授权失败时不会创建游戏进程。
- Velocity 插件异步校验正版 UUID、账号状态、服务器状态、LuckPerms 等级和单服例外规则，支持 `disabled`、`monitor`、`enforce` 三种模式。
- Windows 只读采集器每分钟通过 Minecraft 状态协议查询各 Velocity 目标；不持有 RCON、进程控制或服务器启停权限。
- `Administrator` 可从启动器申请 90 秒一次性后台票据；票据只放 URL fragment，兑换后改用 `HttpOnly`、`Secure`、`SameSite=Strict` 的独立浏览器会话，不把启动器 Bearer 交给网页。
- 管理后台强制 TOTP 双重验证，提供一次性恢复码和 CSRF 防护；支持服务器新增、编辑、归档、恢复和维护状态，所有变更使用修订号并在同一事务中写入审计日志。
- 启动器 API `0.6.0` 已通过 `https://launcher-api.hechao.world` 上线；目录会合并实时在线人数，并在心跳过期或端口关闭时显示关闭。

启动器 `0.7.0` 与 API `0.8.0` 的管理后台当前只在源码中完成，尚未部署；线上仍为 API `0.6.0`。Microsoft 公共客户端应用已经注册并内置 Client ID；Minecraft Java API 访问许可已于 2026-07-22 提交申请，当前等待审核，因此生产目录强制登录开关保持关闭。Velocity 插件已放入代理插件目录并保持 `monitor`，将在管理员下一次手动重启 Velocity 后加载；本次开发没有重启任何 Minecraft 进程。客户端不会使用第三方启动器凭据，也不包含客户端密码。

## 项目结构

- `src/Hechao.Launcher`：WPF 桌面客户端、视图模型、本地设置和演示服务。
- `src/Hechao.Contracts`：服务器目录、客户端档案、权限等级和 API 接口模型。
- `src/Hechao.Distribution`：签名清单、路径策略、断点续传、哈希校验、安装与回滚核心。
- `src/Hechao.Publisher`：管理员离线生成密钥、对象文件和签名清单，并使用 DPAPI 凭据上传不可变 OSS 对象的命令行工具。
- `src/Hechao.Api`：独立启动器 API、管理员 Web 控制台、MFA、目录 CRUD 与审计；只监听 `127.0.0.1:8090`，由 Nginx 终止公网 TLS。
- `src/Hechao.StatusCollector`：游戏 VPS 上的只读 Minecraft 状态采集器，使用机器级 DPAPI 保护内部令牌。
- `src/Hechao.VelocityAuthorizer`：Velocity 3.4 / Java 21 异步进服授权插件。
- `tests/Hechao.Distribution.Tests`：签名、路径、续传、跨域令牌隔离、坏哈希、并发锁和原子回滚测试。
- `tests/Hechao.Api.Tests`：目录摘要锚定、OSS V4 预签名 URL 和进服授权规则测试。
- `tests/Hechao.StatusCollector.Tests`：Minecraft 状态协议、失效目标隔离和心跳批次测试。
- `deploy/linux`：阿里云上的 systemd、PostgreSQL、备份、发布脚本和 Nginx 模板，不包含密码或密钥。
- `deploy/windows/luckperms-sync`：游戏 VPS 的只读 LuckPerms 同步桥与计划任务安装脚本。
- `deploy/windows/server-heartbeats`：一分钟只读状态计划任务、配置样例和 DPAPI 令牌保护脚本。
- `deploy/windows/velocity-authorizer`：只备份和安装插件/配置、不重启 Velocity 的部署脚本。

## 本地构建

需要 .NET 10 SDK 和 Windows。

```powershell
dotnet build Hechao.Launcher.sln -c Release
dotnet test Hechao.Launcher.sln -c Release
dotnet publish src\Hechao.Launcher\Hechao.Launcher.csproj -c Release -p:PublishProfile=win-x64 -o artifacts\publish\win-x64
dotnet publish src\Hechao.Api\Hechao.Api.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\api-linux-x64
dotnet publish src\Hechao.StatusCollector\Hechao.StatusCollector.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\status-collector-win-x64
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.VelocityAuthorizer clean test jar --no-daemon
```

## 接入顺序

1. 已使用独立只写 RAM 身份上传 `base-1.21.11` 的 `4,900` 个不可变对象，并原子发布 API `0.6.0`、签名清单、目录记录和实时心跳。
2. 等待 Minecraft Java API 访问许可审核通过并完成真实账号验收。
3. 由管理员在维护窗口手动重启 Velocity，使 `monitor` 模式插件加载；核对代理目标与平台目录后观察授权日志。
4. 使用普通、VIP、管理员和服主正版账号完成下载、安装、Java 运行时准备及单服权限验收。
5. 验收通过后把 Velocity 切到 `enforce`，再启用目录强制登录。
6. 部署 API `0.8.0` 与启动器 `0.7.0`，完成管理员票据、MFA 恢复码、目录变更和审计灰度验收。

当前工程不包含 VPS 密钥、服务器管理权限或远程启停代码。

## 实施文档

完整的平台架构、HTTPS 迁移、客户端下载、权限、管理后台和分阶段任务见 [`docs/PLATFORM_PLAN.md`](docs/PLATFORM_PLAN.md)。管理员浏览器登录与 MFA 见 [`docs/ADMIN_WEB_OPERATIONS.md`](docs/ADMIN_WEB_OPERATIONS.md)，目录 API 边界见 [`docs/ADMIN_CATALOG_OPERATIONS.md`](docs/ADMIN_CATALOG_OPERATIONS.md)。客户端发布与密钥边界见 [`docs/DISTRIBUTION_OPERATIONS.md`](docs/DISTRIBUTION_OPERATIONS.md)。Microsoft/LuckPerms 激活与运维见 [`docs/AUTHENTICATION_OPERATIONS.md`](docs/AUTHENTICATION_OPERATIONS.md)。Velocity 最终授权见 [`docs/VELOCITY_AUTHORIZATION_OPERATIONS.md`](docs/VELOCITY_AUTHORIZATION_OPERATIONS.md)。只读状态采集见 [`docs/SERVER_HEARTBEAT_OPERATIONS.md`](docs/SERVER_HEARTBEAT_OPERATIONS.md)。实时无密码资产基线见 [`docs/ASSET_INVENTORY.md`](docs/ASSET_INVENTORY.md)，API 发布与回滚见 [`docs/API_OPERATIONS.md`](docs/API_OPERATIONS.md)，数据库备份与恢复边界见 [`docs/DATABASE_OPERATIONS.md`](docs/DATABASE_OPERATIONS.md)，版本与 Git 规则见 [`docs/RELEASE_AND_GIT_WORKFLOW.md`](docs/RELEASE_AND_GIT_WORKFLOW.md)。

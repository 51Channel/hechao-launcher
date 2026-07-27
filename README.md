# 赫朝启动器

赫朝 Minecraft 社区的 Windows 桌面启动器。当前启动器私有 OSS 灰度候选为 `0.11.14`，API 生产版本为 `0.20.0`。平台已经完成 C 版响应式视觉系统、赫朝账号、Microsoft/Minecraft 正版绑定、HTTPS 服务器目录、LuckPerms 等级同步与受控修改、权限过滤、签名客户端分发、平滑并行断点续传、SHA-256 校验、修复、主动回滚、原子版本切换、每档案独立 `.minecraft`、共享下载对象、每档案受管 Java 与自定义 Java、Windows 安装包、真实 Minecraft 启动、本地脱敏诊断及玩家确认上传、隐私受限运行遥测、Velocity 服务端二次授权、只读实时状态与进程指标采集、统一运行告警，以及带独立浏览器会话、双重验证、活动排期、玩家搜索、单服权限规则、论坛会话联动和账号安全操作的管理员控制台。

由赫朝独立运营。非 Minecraft 官方产品。未经 Mojang 或 Microsoft 批准，也不与 Mojang 或 Microsoft 关联。

## 当前能力

- 展示大厅、生存服和活动服的状态、在线人数、核心与 Minecraft 版本。
- 提供服务器、下载、活动、赫朝账户和设置五个真实工作区；短屏与高 DPI 下使用受约束布局和局部滚动，不裁切运行参数。
- 使用 IconPark 官方轮廓图标统一功能按钮与状态图形；界面优先使用系统已安装的苹方字体，并在不可用时回退到微软雅黑。
- 先注册或登录赫朝账号，再独立绑定 Microsoft/Minecraft Java 正版身份；旧版 Microsoft 临时账户可在验证同一正版身份后安全并入正式赫朝账号。
- 切换服务器并根据在线/维护状态控制主操作按钮。
- 读取经 ECDSA P-256 签名的客户端清单；未知公钥、篡改负载、危险路径和远程明文 HTTP 会被拒绝。
- 使用最多 16 路受控并行、HTTP Range 断点续传和 SHA-256 逐文件校验；重复摘要只下载一次，下载失败时保留 `.part` 供下次继续。
- 在独立暂存目录构建完整客户端，通过目录重命名切换活动版本，并保留一个 `.previous` 版本供回滚。
- 玩家可在退出对应 Minecraft 后主动回滚到上一版本；启动器会使用同一跨进程锁、硬链接优先的独立暂存副本和原子目录交换，并把当前存档、截图、设置与服务器列表带入回滚版本。
- 使用安装式启动器和独立游戏数据根目录；每个客户端档案拥有自己的 `instances\<profile-id>\.minecraft` 与 `runtime`，下载对象跨档案共享，Java 默认随对应档案安装并允许单独改为玩家选择的兼容运行时。
- 首次运行自动迁移旧 `%AppData%\Hechao\instances` 或自定义客户端根目录；迁移失败时保留原目录并停止启动，不静默切换到空数据。
- Windows 安装包按当前用户安装到 `%LocalAppData%\Programs\Hechao Launcher`；升级和卸载均保留游戏数据。
- 修复流程会重新检查本地文件；同档案的并发安装通过跨进程独占锁阻止。
- 提供实时下载任务、持久化历史、取消任务、活动服目录、客户端修复入口和完整设置页。
- “启动时检查客户端更新”可关闭首次本地扫描，但进入服务器前仍强制检查；重新开启时立即检查当前档案。
- 将所选服务器、内存、游戏数据目录、默认页面、缓存与启动行为保存到 `%LocalAppData%\Hechao\Launcher\settings.json`。
- 通过 `IServerCatalogClient` 从 HTTPS API 读取服务器目录，并按“在线 API、上次成功缓存、内置应急目录”顺序降级。
- 使用赫朝账号建立社区会话；绑定游戏身份时使用系统浏览器执行 Microsoft OAuth 与 PKCE，再通过 Xbox/XSTS/Minecraft 验证 Java 正版权益。
- 进入服务器时优先静默续期 Minecraft 游戏会话；缓存无法续期时自动打开系统浏览器刷新 Microsoft 凭据，校验所选账号与已绑定 Minecraft UUID 一致后继续启动，不要求退出赫朝账号。
- 使用 15 分钟访问令牌和可撤销、轮换的刷新令牌；刷新会话由 Windows DPAPI 保护。
- 账户页支持退出当前设备、原子撤销全部设备及后台会话，并在校验当前赫朝密码后解除 Minecraft 身份绑定；解除绑定会撤销全部会话和待使用进服授权，并把等级回退为 `Member`。
- 从共享 LuckPerms 数据库每 5 分钟同步主组，按 `Member`、`Participant`、`Collaborator`、`Administrator` 过滤目录。
- 私有 OSS 下载通过启动器 API 鉴权；API 仅为清单内对象签发 5 分钟 V4 URL，Bearer 不会随跳转发送到 OSS。
- 生产发布公钥已内嵌，启动器只信任 `release-2026-07-primary`；私钥使用 Windows DPAPI 加密离线保存，并已通过 RSA/AES-GCM 加密恢复包完成真实恢复和验签演练。
- 使用每档案受管 Java 运行时和签名档案构建正版会话，直接连接 `mc.hehe11.fun`；基础 Fabric、纯 Vanilla、Forge `47.4.0`、NeoForge `21.11.42`、PVP Fabric 和 DollNight 六套档案均已正式发布。
- 记录 Minecraft 正常或异常退出；玩家可在设置页主动生成脱敏、限大小的本地诊断包，世界存档和账号凭据不会进入 ZIP，文件不会自动上传。
- 在 Minecraft 进程启动前申请 10 分钟、一次性 Velocity 启动授权；授权失败时不会创建游戏进程。
- Velocity 插件异步校验正版 UUID、账号状态、服务器状态、LuckPerms 等级和单服例外规则，支持 `disabled`、`monitor`、`enforce` 三种模式；首次连接会以一次性启动授权选择的目标为准，把初始大厅路由改写到对应后端服。
- Windows 只读采集器每分钟查询各 Velocity 目标，并可按本机监听端口读取 Java 进程内存、CPU、启动时间和磁盘余量；Paper/Purpur 指标代理只把 TPS、MSPT 与累计 GC 时间原子写入本地 JSON。两者都不持有 RCON、控制台或服务器启停权限。
- `Administrator` 可从启动器申请 90 秒一次性后台票据；票据只放 URL fragment，兑换后改用 `HttpOnly`、`Secure`、`SameSite=Strict` 的独立浏览器会话，不把启动器 Bearer 交给网页。
- 管理后台强制 TOTP 双重验证，提供一次性恢复码和 CSRF 防护；支持服务器新增、编辑、归档、恢复、公告、开放排期、玩家搜索、访问预览和单服规则，所有变更使用修订号并在同一事务中写入审计日志。
- 管理后台可排队四个固定 LuckPerms 全局组的等级变更；大厅代理通过 LuckPerms API 应用，不直接写 MariaDB，也不接受任意控制台命令。
- 全部认证状态撤销和 UUID 封禁会通过可靠 outbox 联动论坛 `sessionVersion`，使已经签发的论坛 Cookie 失效。
- 启动器 API `0.20.0` 已通过 `https://launcher-api.hechao.world` 上线；对象签名入口使用独立令牌桶，登录与全局防刷限制保持分离。
- API 每分钟评估 5xx、延迟、登录失败、下载失败和服务器运行状态；独立监控器检查公网入口、私有 OSS 基线、TLS 证书与异地备份状态，只在新告警、级别变化和恢复时发送邮件，不控制游戏服进程。

API `0.20.0-20260727T011953Z` 已完成隔离生产备份还原、迁移 17、哈希校验、原子切换和公网回归；账号安全、论坛 Cookie 联动、客户端三通道、隐私受限遥测、服务器运行指标和统一告警均在线。状态采集器 `0.2.0` 已实时上报大厅、Survival1、Survival2 的进程内存、CPU、启动时间和磁盘容量；三个 Paper/Purpur 指标代理文件已部署，安装未改变 Java PID，等待服主下一次自行重启后加载 TPS/MSPT/GC。大厅 LuckPerms 等级代理同样等待下次自行重启后做真实改回测试。Velocity 授权插件 `0.2.0` 已加载为 `monitor`，六个生产目录项覆盖 `lobby`、`survival1`、`survival2`、`activity`、`pvp` 与 DollNight。当前测试为 `.NET 348/348`、Velocity `11/11`、等级代理 `4/4`、指标代理 `2/2`。真实管理员已完成 MFA 登记，生产凭据 `1`、恢复码哈希 `8`、有效 MFA 会话 `1`；`0.11.14` 已产生首条真实启动遥测，诊断编号 `1e707520` 的上传、管理员下载、审计和本地 SHA-256 复验均已完成。真实四级账号验收尚未完成，因此 `Authentication__EnforceCatalogAuthentication=false` 与 Velocity `monitor` 暂时保持不变。VSS 世界备份引擎已部署，锁文件读取、后台 ZIP、条目、SHA-256、状态与卷影清理闭环通过，三服错峰计划已写入磁盘，首次 Essentials 正式世界归档仍待验收。数据库异地加密链已完成真实 OSS 往返、定时任务、告警恢复与异地主机隔离恢复；论坛和 Sub2API 异地链仍等待 RAM v5。客户端不会使用第三方启动器凭据，不采集 Microsoft 密码，也不保存赫朝账号密码。

## 项目结构

- `src/Hechao.Launcher`：WPF 桌面客户端、视图模型、本地设置和演示服务。
- `src/Hechao.Contracts`：服务器目录、客户端档案、权限等级和 API 接口模型。
- `src/Hechao.Distribution`：签名清单、路径策略、断点续传、哈希校验、安装与回滚核心。
- `src/Hechao.Publisher`：管理员离线生成密钥、内容寻址对象和签名清单，并使用 DPAPI 凭据上传 OSS 对象的命令行工具。
- `src/Hechao.Backup`：数据库与签名恢复材料的 RSA/AES-GCM 加密信封、私有 OSS 不可覆盖上传和下载复验工具。
- `src/Hechao.Api`：独立启动器 API、管理员 Web 控制台、MFA、目录 CRUD 与审计；只监听 `127.0.0.1:8090`，由 Nginx 终止公网 TLS。
- `src/Hechao.StatusCollector`：游戏 VPS 上的只读 Minecraft 状态采集器，使用机器级 DPAPI 保护内部令牌。
- `src/Hechao.ServerMetricsAgent`：Paper/Purpur 只读 TPS、MSPT 与 GC 本地指标代理。
- `src/Hechao.VelocityAuthorizer`：Velocity 3.4 / Java 21 异步进服授权插件。
- `src/Hechao.LuckPermsTierAgent`：大厅 Paper / Java 21 受控全局等级代理。
- `installer`：NSIS 3 简体中文/英文安装脚本。
- `tools/Build-WindowsInstaller.ps1`：测试、发布、安装包编译和 SHA-256 生成入口。
- `tests/Hechao.Distribution.Tests`：签名、路径、续传、跨域令牌隔离、坏哈希、并发锁和原子回滚测试。
- `tests/Hechao.Api.Tests`：目录摘要锚定、OSS V4 预签名 URL 和进服授权规则测试。
- `tests/Hechao.StatusCollector.Tests`：Minecraft 状态协议、失效目标隔离和心跳批次测试。
- `deploy/linux`：阿里云上的 systemd、PostgreSQL、备份、发布脚本和 Nginx 模板，不包含密码或密钥。
- `deploy/windows/luckperms-sync`：游戏 VPS 的只读 LuckPerms 同步桥与计划任务安装脚本。
- `deploy/windows/server-heartbeats`：一分钟只读状态计划任务、配置样例和 DPAPI 令牌保护脚本。
- `deploy/windows/velocity-authorizer`：只备份和安装插件/配置、不重启 Velocity 的部署脚本。
- `deploy/windows/world-backup`：串行、校验、原子完成并按服保留的世界备份引擎及三个服务端包装脚本。

## 本地构建

需要 .NET 10 SDK 和 Windows。构建脚本会优先使用仓库根目录的 `.dotnet\dotnet.exe`，不存在时再使用系统 SDK；本机工具目录不会进入 Git。

```powershell
dotnet build Hechao.Launcher.sln -c Release
dotnet test Hechao.Launcher.sln -c Release
dotnet publish src\Hechao.Launcher\Hechao.Launcher.csproj -c Release -p:PublishProfile=win-x64 -o artifacts\publish\win-x64
.\tools\Build-WindowsInstaller.ps1
dotnet publish src\Hechao.Api\Hechao.Api.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\api-linux-x64
dotnet publish src\Hechao.StatusCollector\Hechao.StatusCollector.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\status-collector-win-x64
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.VelocityAuthorizer clean test jar --no-daemon
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.ServerMetricsAgent clean test jar --no-daemon
```

## 接入顺序

1. 已使用独立写入 RAM 身份发布 `base-1.21.11` / `1.0.5`、`activity-neoforge-1.21.11` / `1.0.10` 与 `pvp-fabric-1.20.1` / `1.0.0`，并原子激活签名清单、目录记录和实时心跳；活动服保持关闭。
2. [审核已完成] 管理员于 2026-07-26 确认 Minecraft Java API 访问许可已经通过；仍需完成真实账号验收。
3. [已完成] `hechao-velocity-authorizer 0.2.0` 已以 `monitor` 模式加载；全部代理目标已经登记，生产合成授权已验证一次性授权选择的后端目标能够改写初始大厅路由。
4. 使用普通、VIP、管理员和服主正版账号完成下载、安装、每档案 Java 运行时准备及单服权限验收。
5. 验收通过后把 Velocity 切到 `enforce`，再启用目录强制登录。
6. [已完成] 部署 API `0.20.0`、统一运行告警与状态采集器 `0.2.0`；赫朝账号、对象分发、下载专用限流、授权定向路由、诊断上传、服务器排期、单服访问规则、论坛会话联动、受控全局等级、运行遥测和服务器进程/磁盘指标已上线。真实管理员 MFA 与诊断管理员下载审计已完成；大厅等级代理和三个指标代理仍需分别完成真实验收。
7. 启动器 `0.11.14` 已替换 `0.11.13` 进入私有 OSS 灰度；按 [`docs/PRELAUNCH_PILOT_0.11.14.md`](docs/PRELAUNCH_PILOT_0.11.14.md) 验证启动检查开关，并回归登录、安装、修复、回滚、诊断上传与真实游戏启动。

当前工程不包含 VPS 密钥、服务器管理权限或远程启停代码。

## 实施文档

功能、生产验收与外部依赖的权威状态见 [`docs/COMPLETION_MATRIX.md`](docs/COMPLETION_MATRIX.md)。完整的平台架构、HTTPS 迁移、客户端下载、权限、管理后台和分阶段任务见 [`docs/PLATFORM_PLAN.md`](docs/PLATFORM_PLAN.md)。玩家安装、迁移、修复与隐私说明见 [`docs/PLAYER_INSTALLATION_GUIDE.md`](docs/PLAYER_INSTALLATION_GUIDE.md)，管理员构建、灰度、发布与回滚流程见 [`docs/ADMIN_RELEASE_RUNBOOK.md`](docs/ADMIN_RELEASE_RUNBOOK.md)。Windows 安装包、数据目录、旧版迁移与卸载边界见 [`docs/WINDOWS_INSTALLER_AND_STORAGE.md`](docs/WINDOWS_INSTALLER_AND_STORAGE.md)，游戏退出与隐私诊断规则见 [`docs/GAME_DIAGNOSTICS.md`](docs/GAME_DIAGNOSTICS.md)。管理员浏览器登录与 MFA 见 [`docs/ADMIN_WEB_OPERATIONS.md`](docs/ADMIN_WEB_OPERATIONS.md)，账号停用、会话撤销和 UUID 封禁见 [`docs/ADMIN_ACCOUNT_SECURITY_OPERATIONS.md`](docs/ADMIN_ACCOUNT_SECURITY_OPERATIONS.md)，目录 API 边界见 [`docs/ADMIN_CATALOG_OPERATIONS.md`](docs/ADMIN_CATALOG_OPERATIONS.md)。客户端发布与密钥边界见 [`docs/DISTRIBUTION_OPERATIONS.md`](docs/DISTRIBUTION_OPERATIONS.md)。Microsoft/LuckPerms 激活与运维见 [`docs/AUTHENTICATION_OPERATIONS.md`](docs/AUTHENTICATION_OPERATIONS.md)。Velocity 最终授权见 [`docs/VELOCITY_AUTHORIZATION_OPERATIONS.md`](docs/VELOCITY_AUTHORIZATION_OPERATIONS.md)。只读状态采集见 [`docs/SERVER_HEARTBEAT_OPERATIONS.md`](docs/SERVER_HEARTBEAT_OPERATIONS.md)，深度运行指标见 [`docs/SERVER_RUNTIME_METRICS_OPERATIONS.md`](docs/SERVER_RUNTIME_METRICS_OPERATIONS.md)，统一告警见 [`docs/OPERATIONAL_ALERTS.md`](docs/OPERATIONAL_ALERTS.md)，世界备份见 [`docs/WORLD_BACKUP_OPERATIONS.md`](docs/WORLD_BACKUP_OPERATIONS.md)。实时无密码资产基线见 [`docs/ASSET_INVENTORY.md`](docs/ASSET_INVENTORY.md)，API 发布与回滚见 [`docs/API_OPERATIONS.md`](docs/API_OPERATIONS.md)，数据库本机备份见 [`docs/DATABASE_OPERATIONS.md`](docs/DATABASE_OPERATIONS.md)，异地加密备份与恢复见 [`docs/OFFSITE_BACKUP_AND_RECOVERY.md`](docs/OFFSITE_BACKUP_AND_RECOVERY.md)，版本与 Git 规则见 [`docs/RELEASE_AND_GIT_WORKFLOW.md`](docs/RELEASE_AND_GIT_WORKFLOW.md)。

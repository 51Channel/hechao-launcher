# 赫朝启动器

赫朝 Minecraft 社区的 Windows 桌面启动器。当前生产为启动器 `0.12.3`、API `0.22.0`、Velocity Authorizer `0.4.0`（`monitor`）和 Lobby Guard `0.1.0`；四个制品、可回滚部署和自动验收均已完成，剩余门槛只涉及真实四级账号与 `2/3/5/20` 人逐级灰度。平台已经完成 C 版响应式视觉系统、赫朝账号、Microsoft/Minecraft 正版绑定、HTTPS 服务器目录、LuckPerms 等级同步与受控修改、权限过滤、签名客户端分发、平滑并行断点续传、SHA-256 校验、修复、主动回滚、原子版本切换、每档案独立 `.minecraft`、共享下载对象、每档案受管 Java 与自定义 Java、Windows 安装包、真实 Minecraft 启动、本地脱敏诊断及玩家确认上传、隐私受限运行遥测、Velocity 服务端二次授权、只读实时状态与进程指标采集、统一运行告警，以及带独立浏览器会话、双重验证、活动排期、玩家搜索、单服权限规则、论坛会话联动和账号安全操作的管理员控制台。

2026-07-29 已确认新架构：赫朝启动器成为唯一服务器选择和切换入口；大厅继续作为 LuckPerms 等前置能力的内部承载器，但不再向玩家展示、授权、路由或回退。Velocity 继续负责统一公网入口、forwarding 和服务端二次授权。完整约束、回滚和验收标准见 [`docs/LAUNCHER_ONLY_SERVER_SWITCHING.md`](docs/LAUNCHER_ONLY_SERVER_SWITCHING.md)。

由赫朝独立运营。非 Minecraft 官方产品。未经 Mojang 或 Microsoft 批准，也不与 Mojang 或 Microsoft 关联。

## 当前能力

- 展示生存服和活动服的状态、在线人数、核心与 Minecraft 版本；基础设施大厅只在管理员运维视图中可见，不进入玩家目录。
- 提供服务器、下载、活动、赫朝账户和设置五个真实工作区；短屏与高 DPI 下使用受约束布局和局部滚动，不裁切运行参数。
- 使用 IconPark 官方轮廓图标统一功能按钮与状态图形；界面优先使用系统已安装的苹方字体，并在不可用时回退到微软雅黑。
- 先注册或登录赫朝账号，再独立绑定 Microsoft/Minecraft Java 正版身份；旧版 Microsoft 临时账户可在验证同一正版身份后安全并入正式赫朝账号。
- 由启动器独占服务器切换，根据在线/维护状态控制主操作；已有游戏运行时先安全退出，再使用新授权启动目标档案。
- 读取经 ECDSA P-256 签名的客户端清单；未知公钥、篡改负载、危险路径和远程明文 HTTP 会被拒绝。
- 使用最多 16 路受控并行、HTTP Range 断点续传和 SHA-256 逐文件校验；重复摘要只下载一次，下载失败时保留 `.part` 供下次继续。
- 在独立暂存目录构建完整客户端，通过目录重命名切换活动版本，并保留一个 `.previous` 版本供回滚。
- 玩家可在退出对应 Minecraft 后主动回滚到上一版本；启动器会使用同一跨进程锁、硬链接优先的独立暂存副本和原子目录交换，并把当前存档、截图、设置与服务器列表带入回滚版本。
- 使用安装式启动器和独立游戏数据根目录；每个客户端档案拥有自己的 `instances\<profile-id>\.minecraft` 与 `runtime`，下载对象跨档案共享，Java 默认随对应档案安装并允许单独改为玩家选择的兼容运行时。Windows 特殊字符路径会为游戏工作目录、Java 和原生库分别选择兼容路径，不移动或复制玩家档案。
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
- 使用每档案受管 Java 运行时和签名档案构建正版会话，直接连接 `mc.hehe11.fun`；基础 Fabric、纯 Vanilla、Forge `47.4.0`、NeoForge `21.11.42`、恐怖整蛊 Fabric（历史档案 ID 为 `pvp-fabric-1.20.1`）和 DollNight 六套档案均已正式发布。
- 记录 Minecraft 正常或异常退出；玩家可在设置页主动生成脱敏、限大小的本地诊断包，世界存档和账号凭据不会进入 ZIP，文件不会自动上传。
- 在 Minecraft 进程启动前申请 10 分钟、一次性 Velocity 启动授权；授权失败时不会创建游戏进程。
- Velocity 插件异步校验正版 UUID、账号状态、服务器状态、LuckPerms 等级和单服例外规则，支持 `disabled`、`monitor`、`enforce` 三种模式；首次连接只接受一次性启动授权指定的目标，不再依赖或回退到游戏大厅。
- Windows 只读采集器每分钟查询各 Velocity 目标，并可按本机监听端口读取 Java 进程内存、CPU、启动时间和磁盘余量；Paper/Purpur 指标代理只把 TPS、MSPT 与累计 GC 时间原子写入本地 JSON。两者都不持有 RCON、控制台或服务器启停权限。
- `Administrator` 可从启动器申请 90 秒一次性后台票据；票据只放 URL fragment，兑换后改用 `HttpOnly`、`Secure`、`SameSite=Strict` 的独立浏览器会话，不把启动器 Bearer 交给网页。
- 管理后台强制 TOTP 双重验证，提供一次性恢复码和 CSRF 防护；支持服务器新增、编辑、归档、恢复、公告、开放排期、玩家搜索、访问预览和单服规则，所有变更使用修订号并在同一事务中写入审计日志。
- 管理后台可排队四个固定 LuckPerms 全局组的等级变更；大厅代理通过 LuckPerms API 应用，不直接写 MariaDB，也不接受任意控制台命令。
- 全部认证状态撤销和 UUID 封禁会通过可靠 outbox 联动论坛 `sessionVersion`，使已经签发的论坛 Cookie 失效。
- 启动器 API 生产版本 `0.22.0` 已通过 `https://launcher-api.hechao.world` 上线；玩家服务器与内部基础设施角色已拆分，大厅隐藏后仍保留监控。对象签名入口使用独立令牌桶，登录与全局防刷限制保持分离。
- API 私有对象重定向不会把短时 OSS 签名 URL 写入 journal；Nginx 访问日志只保留无查询参数的路径，不记录 Referer，避免密码重置和 OAuth 参数进入日志。
- API 每分钟评估 5xx、延迟、登录失败、下载失败和服务器运行状态；独立监控器检查公网入口、私有 OSS 基线、TLS 证书与异地备份状态，只在新告警、级别变化和恢复时发送邮件，不控制游戏服进程。

API `0.22.0-20260729T144953Z` 已完成一致性备份、哈希校验、迁移 `019`、原子切换、公网回归和大厅基础设施角色验收；`/healthz` 与 `/readyz` 当前均正常，公开目录对 `lobby` 为零命中。账号安全、论坛 Cookie 联动、客户端三通道、隐私受限遥测、服务器运行指标和统一告警均在线。Nginx 五个站点入口已启用无查询参数、无 Referer 的访问日志，合成重置 token 回归泄漏数为 `0`。状态采集器 `0.2.1` 与三类指标代理已实时上报大厅、Survival1、Survival2、Activity 和恐怖整蛊（历史目标 `pvp`）的进程、磁盘、TPS、MSPT 与累计 GC；Activity 零玩家时的 NeoForge 暂停会显式显示为空服暂停，不再误报指标过期。当前仅完成单用户空载基线，不替代多人负载验收。大厅 LuckPerms 等级代理、Lobby Guard `0.1.0` 和指标代理均已加载。生产 Velocity Authorizer `0.4.0` 保持 `monitor`，所有首次连接故障硬拒绝并永久拒绝基础设施目标。当前发布测试为 `.NET 392/392`、Velocity `26/26`、Lobby Guard `3/3`、等级代理 `4/4`、指标代理 `2/2`。

真实管理员已完成 MFA 登记，`0.11.14` 已产生首条真实启动遥测，诊断上传、管理员下载、审计和本地 SHA-256 复验均已完成。基础客户端的 Lobby、Survival1、Survival2、Activity 与恐怖整蛊历史单账号首次路由均已通过；恐怖整蛊的 CrossStitch 修复、身份转发、直连拒绝、稳定连接和正常退出也已验收。Activity 在含 U+200C 的既有数据根目录下已由 `0.12.3` 改用 `%LocalAppData%\Hechao\Launcher\native-runs` 物理目录：`java.library.path`、`org.lwjgl.librarypath`、JNA、LWJGL 解压和 Netty 五个属性唯一指向该目录，不再依赖可能被 Windows 原生加载器解析回真实目标的目录联接。安装版启动器从“进入服务器”完成正版会话、连接 `mc.hehe11.fun`、进入 Activity 世界并以退出码 `0` 正常结束，全程未复现 `UnsatisfiedLinkError` 或 `Can't find dependent libraries`。同档案三轮 fresh grant 重进、NeoForge/Paper 跨档案三轮切换、15 分钟单进程采样、启动器重启接管、强制异常退出和新授权恢复也已用同一真实账号通过，全程未出现 Lobby 回退；Activity 运行时选择维护中的 DollNight 或已关闭的 Survival1，主操作均禁用且现有 PID 不变。跨版本回大厅曾在 API `0.21.0` 和 Velocity 4 隔离环境完成五轮真实客户端验证，相关证据仅保留用于审计；2026-07-29 的新架构已经取消 `/hub`、NPC 和 Via 回大厅方案。生产代理已迁移至 Velocity 4、独立 Java 25 和 Authorizer `0.4.0` monitor；API `0.22.0`、Lobby Guard `0.1.0`、旧回程移除及后端 `/hub` 禁用均已落地。大厅八个玩家交互 Skript 已在线禁用并保留哈希备份，只留每日备份；公网 `25566` 不可达，owl5 与 owl9 恐怖整蛊均无活动的旧转服路径。下一步只按 [`docs/PRELAUNCH_PILOT_0.12.3.md`](docs/PRELAUNCH_PILOT_0.12.3.md) 完成真实四级账号、离线/无权限拒绝、`enforce`、目录强制登录和 `2/3/5/20` 人灰度。

三份 Paper 世界正式归档、远端 ZIP/旁车复核、异机完整解压、`level.dat` 校验和确定性区域抽样恢复已通过；owl9 恐怖整蛊另完成正式 VSS 归档、`2,493/2,493` 文件哈希比对和 `2,370/2,370` 区域全量恢复检查，真正 PVP 未触碰。RAM v5 已默认生效；启动器数据库、论坛与 Sub2API 的异地加密链均已完成真实 OSS 往返、定时任务、告警恢复与异地主机隔离恢复。六个活动签名档案的 `8,944` 个去重对象也已建立 OSS 外完整副本，并通过远端全量哈希和隔离恢复。平台监控器 `0.1.2` 已生产运行。客户端不会使用第三方启动器凭据，不采集 Microsoft 密码，也不保存赫朝账号密码。

## 项目结构

- `src/Hechao.Launcher`：WPF 桌面客户端、视图模型、本地设置和演示服务。
- `src/Hechao.Contracts`：服务器目录、客户端档案、权限等级和 API 接口模型。
- `src/Hechao.Distribution`：签名清单、路径策略、断点续传、哈希校验、安装与回滚核心。
- `src/Hechao.Publisher`：管理员离线生成密钥、内容寻址对象和签名清单，并使用 DPAPI 凭据上传 OSS 对象的命令行工具。
- `src/Hechao.Backup`：数据库与签名恢复材料的 RSA/AES-GCM 加密信封、私有 OSS 不可覆盖上传和下载复验工具。
- `src/Hechao.Api`：独立启动器 API、管理员 Web 控制台、MFA、目录 CRUD 与审计；只监听 `127.0.0.1:8090`，由 Nginx 终止公网 TLS。
- `src/Hechao.StatusCollector`：游戏 VPS 上的只读 Minecraft 状态采集器，使用机器级 DPAPI 保护内部令牌。
- `src/Hechao.ServerMetricsAgent`：Paper/Purpur 只读 TPS、MSPT 与 GC 本地指标代理。
- `src/Hechao.VelocityAuthorizer`：Velocity 4 / Java 25 生产运行、向下兼容测试环境的异步进服授权插件。
- `src/Hechao.LuckPermsTierAgent`：大厅 Paper / Java 21 受控全局等级代理。
- `src/Hechao.LobbyGuard`：大厅 Paper 后端玩家登录拒绝插件；不修改 LuckPerms、指标或备份。
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

需要 .NET 10 SDK、Windows 和 PowerShell 7。所有本机发布与 Windows VPS 运维脚本统一使用 `pwsh`，不再使用 Windows PowerShell 5.1；完整版本、安装、任务迁移和回滚规则见 [`docs/POWERSHELL_7_OPERATIONS.md`](docs/POWERSHELL_7_OPERATIONS.md)。构建脚本会优先使用仓库根目录的 `.dotnet\dotnet.exe`，不存在时再使用系统 SDK；本机工具目录不会进入 Git。

```powershell
dotnet build Hechao.Launcher.sln -c Release
dotnet test Hechao.Launcher.sln -c Release
dotnet publish src\Hechao.Launcher\Hechao.Launcher.csproj -c Release -p:PublishProfile=win-x64 -o artifacts\publish\win-x64
.\tools\Build-WindowsInstaller.ps1
dotnet publish src\Hechao.Api\Hechao.Api.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\api-linux-x64
dotnet publish src\Hechao.StatusCollector\Hechao.StatusCollector.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\status-collector-win-x64
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.VelocityAuthorizer clean test jar --no-daemon
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.ServerMetricsAgent clean test jar --no-daemon
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.LobbyGuard clean test jar --no-daemon
```

## 接入顺序

1. 已使用独立写入 RAM 身份发布 `base-1.21.11` / `1.0.5`、`activity-neoforge-1.21.11` / `1.0.10` 与 `pvp-fabric-1.20.1` / `1.0.0`，并原子激活签名清单、目录记录和实时心跳；活动服保持关闭。
2. [审核已完成] 管理员于 2026-07-26 确认 Minecraft Java API 访问许可已经通过；仍需完成真实账号验收。
3. [已完成] 生产 Authorizer `0.4.0` 已以 `monitor` 模式加载；内部大厅目标、首次故障关闭和授权目标改写均已部署，Lobby Guard 提供后端独立拒绝。
4. 使用普通、VIP、管理员和服主正版账号完成下载、安装、每档案 Java 运行时准备及单服权限验收。
5. 验收通过后把 Velocity 切到 `enforce`，再启用目录强制登录。
6. [已完成] 部署 API `0.22.0`、私有下载与 Nginx 日志脱敏、统一运行告警及状态采集器 `0.2.1`；赫朝账号、对象分发、下载专用限流、授权定向路由、诊断上传、服务器排期、单服访问规则、论坛会话联动、受控全局等级、运行遥测和服务器进程/磁盘指标均已上线。
7. [已完成自动部署] 启动器 `0.12.3`、API `0.22.0`、Authorizer `0.4.0` 与 Lobby Guard `0.1.0` 已生产发布；API 不可达故障关闭与恢复已通过，继续按 [`docs/PRELAUNCH_PILOT_0.12.3.md`](docs/PRELAUNCH_PILOT_0.12.3.md) 完成真实四级账号、离线/无权限拒绝、Lobby 旁路拒绝和多人灰度。

当前工程不包含 VPS 密钥或服务器凭据。远程切换工具必须使用操作者本机密钥，默认只读，
且不提供 Minecraft 游戏服启停能力。

## 实施文档

真实玩家分档采证、Velocity `enforce` 和目录强制登录的失败关闭切换见
[`docs/GRAY_PILOT_AUTHORIZATION_CUTOVER.md`](docs/GRAY_PILOT_AUTHORIZATION_CUTOVER.md)。

功能、生产验收与外部依赖的权威状态见 [`docs/COMPLETION_MATRIX.md`](docs/COMPLETION_MATRIX.md)。owl9 的恐怖整蛊服与真正 PVP 服边界见 [`docs/OWL9_DUAL_BACKEND_OPERATIONS.md`](docs/OWL9_DUAL_BACKEND_OPERATIONS.md)。完整的平台架构、HTTPS 迁移、客户端下载、权限、管理后台和分阶段任务见 [`docs/PLATFORM_PLAN.md`](docs/PLATFORM_PLAN.md)。玩家安装、迁移、修复与隐私说明见 [`docs/PLAYER_INSTALLATION_GUIDE.md`](docs/PLAYER_INSTALLATION_GUIDE.md)，管理员构建、灰度、发布与回滚流程见 [`docs/ADMIN_RELEASE_RUNBOOK.md`](docs/ADMIN_RELEASE_RUNBOOK.md)。Windows 安装包、数据目录、旧版迁移与卸载边界见 [`docs/WINDOWS_INSTALLER_AND_STORAGE.md`](docs/WINDOWS_INSTALLER_AND_STORAGE.md)，PowerShell 7 运行时与计划任务迁移见 [`docs/POWERSHELL_7_OPERATIONS.md`](docs/POWERSHELL_7_OPERATIONS.md)，游戏退出与隐私诊断规则见 [`docs/GAME_DIAGNOSTICS.md`](docs/GAME_DIAGNOSTICS.md)。管理员浏览器登录与 MFA 见 [`docs/ADMIN_WEB_OPERATIONS.md`](docs/ADMIN_WEB_OPERATIONS.md)，账号停用、会话撤销和 UUID 封禁见 [`docs/ADMIN_ACCOUNT_SECURITY_OPERATIONS.md`](docs/ADMIN_ACCOUNT_SECURITY_OPERATIONS.md)，目录 API 边界见 [`docs/ADMIN_CATALOG_OPERATIONS.md`](docs/ADMIN_CATALOG_OPERATIONS.md)。客户端发布与密钥边界见 [`docs/DISTRIBUTION_OPERATIONS.md`](docs/DISTRIBUTION_OPERATIONS.md)。Microsoft/LuckPerms 激活与运维见 [`docs/AUTHENTICATION_OPERATIONS.md`](docs/AUTHENTICATION_OPERATIONS.md)。Velocity 最终授权见 [`docs/VELOCITY_AUTHORIZATION_OPERATIONS.md`](docs/VELOCITY_AUTHORIZATION_OPERATIONS.md)，代理单层协议转换生产切换见 [`docs/PROXY_PROTOCOL_TRANSLATION_PRODUCTION_OPERATIONS.md`](docs/PROXY_PROTOCOL_TRANSLATION_PRODUCTION_OPERATIONS.md)。只读状态采集见 [`docs/SERVER_HEARTBEAT_OPERATIONS.md`](docs/SERVER_HEARTBEAT_OPERATIONS.md)，深度运行指标见 [`docs/SERVER_RUNTIME_METRICS_OPERATIONS.md`](docs/SERVER_RUNTIME_METRICS_OPERATIONS.md)，统一告警见 [`docs/OPERATIONAL_ALERTS.md`](docs/OPERATIONAL_ALERTS.md)，世界备份见 [`docs/WORLD_BACKUP_OPERATIONS.md`](docs/WORLD_BACKUP_OPERATIONS.md)。实时无密码资产基线见 [`docs/ASSET_INVENTORY.md`](docs/ASSET_INVENTORY.md)，API 发布与回滚见 [`docs/API_OPERATIONS.md`](docs/API_OPERATIONS.md)，数据库本机备份见 [`docs/DATABASE_OPERATIONS.md`](docs/DATABASE_OPERATIONS.md)，异地加密备份与恢复见 [`docs/OFFSITE_BACKUP_AND_RECOVERY.md`](docs/OFFSITE_BACKUP_AND_RECOVERY.md)，版本与 Git 规则见 [`docs/RELEASE_AND_GIT_WORKFLOW.md`](docs/RELEASE_AND_GIT_WORKFLOW.md)。

# 启动器 API 运维与回滚

> 当前线上版本：`0.28.7-20260807T072043Z`
> 当前迁移：`026`
> 当前阶段：跨版本回大厅方案已取消；基础设施大厅隔离已部署，待真实玩家灰度
>
> owl9 边界：API 中现有 server ID `pvp` 实际代表恐怖整蛊服
> `C:\mc\server`，不代表 `E:\MinecraftServer` 的真正 PVP 服；后者尚未登记。

## 1. 运行边界

- systemd 单元：`hechao-launcher-api.service`
- 运行用户：`hechao-api`，无登录 Shell
- 程序目录：`/opt/hechao-launcher-api/releases/<release-id>`
- 当前版本链接：`/opt/hechao-launcher-api/current`
- 监听：`127.0.0.1:8090`
- 公网入口：`https://launcher-api.hechao.world`
- Nginx 站点：`/etc/nginx/sites-available/hechao-launcher.conf`
- TLS 证书：`/etc/nginx/ssl/hechao-launcher/fullchain.pem`
- ACME webroot：`/var/www/hechao-acme`
- 数据库：`127.0.0.1:5433/hechao_launcher`
- 健康检查：`GET /healthz`
- 就绪检查：`GET /readyz`，包含数据库探测
- 服务器目录：`GET /v1/catalog`
- 身份端点：`POST /v1/auth/minecraft/exchange`、`POST /v1/auth/refresh`、`POST /v1/auth/logout`、`GET /v1/me`
- API `0.9.0` 身份端点：`POST /v1/auth/register`、`POST /v1/auth/login`、`POST /v1/auth/minecraft/link`；旧 `minecraft/exchange` 作为迁移兼容入口保留
- API `0.10.1` 身份端点：`POST /v1/auth/logout-all`、`POST /v1/auth/minecraft/unlink`；已部署，不增加数据库迁移
- 启动器进服授权：`POST /v1/velocity/launch-grants`
- Velocity 内部授权：`POST /v1/internal/velocity/authorize`
- LuckPerms 内部端点：`POST /v1/internal/luckperms/snapshot`、等级命令 `claim` 与 `complete`
- 服务器心跳内部端点：`POST /v1/internal/server-heartbeats`
- 管理员票据端点：`POST /v1/admin-auth/tickets`，仅允许 `Administrator` 启动器会话
- 管理员浏览器与目录端点：`/v1/admin-auth/*`、`/v1/admin/*`，仅允许管理域名上的独立 Cookie 会话；目录写入还要求 MFA 与 CSRF。`POST /v1/admin-auth/trusted-device` 只允许已通过 MFA 的会话签发可撤销本机信任，不能替代启动器一次性票据
- 玩家诊断端点：`POST /v1/diagnostics/uploads` 与一次性令牌保护的 `PUT /v1/diagnostics/uploads/{id}`
- 管理员诊断端点：`GET /v1/admin/diagnostics` 与 `GET /v1/admin/diagnostics/{id}/download`，要求 MFA
- 管理员玩家端点：`GET /v1/admin/users`、访问预览、单服规则、受控全局等级、账号停用/恢复、设备会话撤销及 Minecraft UUID 封禁
- 管理员发布端点：`/v1/admin/catalog/client-profiles/*`，包括签名清单导入、三通道、稳定灰度、暂停和回滚
- 启动器运行遥测：`POST /v1/telemetry/events`，仅接受认证会话、固定枚举和最多 50 条的幂等批次
- 管理员遥测汇总：`GET /v1/admin/telemetry/summary?hours=24|168|720`，只返回聚合值并要求 MFA
- 日志：systemd journal

API 不监听公网地址，不开放 UFW 高位端口，也不负责启动或停止 Minecraft 服务端。

认证与分发环境变量位于权限 `600` 的 `/etc/hechao-launcher-api/environment`：

```text
Authentication__EnforceCatalogAuthentication
Authentication__AccessTokenMinutes
Authentication__RefreshTokenDays
Authentication__InternalSyncTokenSha256
VelocityAuthorization__InternalTokenSha256
VelocityAuthorization__LaunchGrantMinutes
VelocityAuthorization__MaximumLuckPermsAgeMinutes
VelocityAuthorization__RequireGrantIpMatch
ServerHeartbeats__InternalTokenSha256
ServerHeartbeats__FreshnessSeconds
Distribution__ManifestDirectory
Distribution__MaximumManifestBytes
Distribution__OssRegion
Distribution__OssBucket
Distribution__OssEndpoint
Distribution__OssObjectPrefix
Distribution__PresignedUrlSeconds
AdminWeb__Enabled
AdminWeb__PublicBaseUrl
AdminWeb__DataProtectionKeyPath
AdminWeb__TicketSeconds
AdminWeb__SessionMinutes
AdminWeb__EnrollmentMinutes
AdminWeb__TrustedDeviceDays
AdminWeb__TotpIssuer
DiagnosticUploads__StorageRoot
DiagnosticUploads__UploadTokenMinutes
DiagnosticUploads__RetentionDays
DiagnosticUploads__MaximumBytes
DiagnosticUploads__MaximumUploadsPerDay
DiagnosticUploads__MaximumBytesPerDay
DiagnosticUploads__MaximumActiveUploads
DiagnosticUploads__CleanupMinutes
ForumSessionRevocation__Enabled
ForumSessionRevocation__BaseUrl
ForumSessionRevocation__InternalToken
ForumSessionRevocation__DeliveryIntervalSeconds
ForumSessionRevocation__RequestTimeoutSeconds
ForumSessionRevocation__LeaseSeconds
ForumSessionRevocation__BatchSize
OSS_ACCESS_KEY_ID
OSS_ACCESS_KEY_SECRET
```

环境文件保存 LuckPerms/Velocity 内部令牌的 SHA-256 和专用 RAM 凭据，权限必须保持 `600`。目录强制登录在 Microsoft 应用许可、真实账号测试和 Velocity `enforce` 验收完成前必须保持 `false`。

`0.4.0` 分发端点：

- `GET /v1/profiles/{profileId}/manifest`：返回与目录 SHA-256 一致的签名清单。
- `GET /v1/profiles/{profileId}/objects/{prefix}/{sha256}`：检查玩家档案权限和清单成员关系后，302 到 5 分钟 OSS V4 URL。

分发配置使用 [`configure-distribution.sh`](../deploy/linux/configure-distribution.sh)。它只写配置，不重启 API。

`2026-07-23` 已将专用只读 RAM 凭据和全部 `Distribution__*` 配置写入环境文件，并保留了写入前的 root-only 备份。API `0.4.0-20260723T051123Z` 部署后已加载该配置，环境文件继续保持 `root:root 600`。

`0.5.0` 进服授权端点：

- `POST /v1/velocity/launch-grants`：为已登录且有目标服权限的玩家创建 10 分钟一次性启动授权。
- `POST /v1/internal/velocity/authorize`：供 Velocity 插件按正版 UUID、服务器状态、LuckPerms 等级、单服例外和启动授权做最终判定。

Velocity 配置使用 [`configure-velocity-authorization.sh`](../deploy/linux/configure-velocity-authorization.sh)。脚本从标准输入读取内部凭据的 SHA-256，备份旧环境文件并保持权限 `600`，但不重启 API。完整激活顺序见 [`VELOCITY_AUTHORIZATION_OPERATIONS.md`](VELOCITY_AUTHORIZATION_OPERATIONS.md)。

`0.5.0` 上线前数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T102842Z.dump`，SHA-256 `f6455e523cebc2ca6ca98d3b0c3ab7eebe4e87489141f3ae4dcf954191e12efc`，`sha256sum -c` 与 `pg_restore --list` 均通过。环境配置备份位于 `/var/backups/hechao-launcher/api-configuration/environment-before-velocity-20260723T103150Z`。

`0.6.0` 新增按 Velocity 目标存储的实时心跳。目录配置为 `Maintenance` 或 `Closed` 时后台状态始终优先；配置为 `Online` 时使用三分钟内的心跳，过期或端口关闭则返回 `Closed`。发布 ID 为 `0.6.0-20260723T123346Z`，归档 SHA-256 为 `FA4FAD6CD5287D3C16596C07189FE5E806F0FFE40D3443743E633803F7CE6442`。迁移 4、心跳鉴权、真实采集和旧域名回归均通过。部署后备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T124326Z.dump` 的 SHA-256 为 `508b37c7a695413e2a3d3d5b7ff08212f720077121bb7237c522957ec08d9464`，`sha256sum -c` 与 `pg_restore --list` 均通过。

`0.7.0` 新增管理员服务器目录 CRUD、归档/恢复、乐观并发修订号和事务内审计日志。迁移 5 只增加 `servers.revision` 与审计目标索引；当前表结构已随 `0.9.0` 部署，回滚到 `0.6.0` 时可以保留这些兼容字段。详细接口、验证和回滚边界见 [`ADMIN_CATALOG_OPERATIONS.md`](ADMIN_CATALOG_OPERATIONS.md)。

`0.8.0` 新增启动器管理员入口、90 秒一次性票据、来源 IP 绑定、独立 `HttpOnly` 浏览器会话、TOTP 与恢复码、防 TOTP 重放、CSRF、管理域名锁定和静态 Web 控制台。迁移 6 已随 `0.9.0` 部署，但生产环境未配置 `AdminWeb__*` 且默认关闭；`admin.hechao.world` 继续返回 404。详细启用与回滚边界见 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md)。

`0.9.0` 新增赫朝账号注册与登录、PBKDF2 密码哈希、账号/邮箱唯一索引、独立 Minecraft 正版身份绑定和旧 `legacy_*` 身份安全接管。迁移 7 为 `launcher.users` 增加 `username`、`email` 与 `password_hash`；回滚到旧 API 时可以保留这些兼容字段，但新建赫朝账号无法使用旧客户端登录。

生产发布 ID 为 `0.9.0-20260723T195253Z`。单文件程序 SHA-256 为 `159DDBA288078E0F2C6DAA4BF3C3A62507EC3A3F99FBEC24D15A78AAB57ADBBA`，上传归档 SHA-256 为 `24B8DBA16BBC8A141C43AB61506EE221E127A457044B9698D7AC06DA017C0241`。发布前数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T195226Z.dump`，大小 `48,720` 字节，SHA-256 为 `621638f3500680e7ad3903cab62ac40a974defe0ecb65a4eb9cfc292cd5547d6`；`sha256sum -c` 与 `pg_restore --list` 均通过。

部署后迁移记录为 `1` 至 `7`，服务、本机 `/healthz`、`/readyz` 与公网检查均通过，journal 无 warning。隔离测试账号完成注册 `201`、本人信息 `200`、目录 `200`、刷新 `200`、刷新令牌重放 `401`、无效 Minecraft 绑定 `401`、退出 `204`、退出后访问 `401`、密码登录 `200`；测试用户、会话与对应审计记录已精确清理，用户总数恢复为 `0`。`hechao.world` 与 `api.hechao.world` 保持 200，`admin.hechao.world` 保持预期的 404。

`0.10.0-20260724T101528Z` 首次上线了全部设备退出和密码确认解除 Minecraft 绑定。单文件程序 SHA-256 为 `ECE445F76682775917D089630B6C0105AEE04707EE08D36886E53514E8CDCB11`，上传归档 SHA-256 为 `020DD8BA3D8D797336B5155F60EC34F900D9B27310FB52085B6BAA1BFEA8A4E6`。发布前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T101600Z.dump` 为 `63,799` 字节，SHA-256 为 `9CEAAEA545525E1A6EC199D11AA62FECAD4E62220641CC847DA2A7D1BB3F64F8`；当前发布与配置备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.10.0-20260724T101600Z.tar.gz` 为 `45,414,481` 字节，SHA-256 为 `1F7935395A99F85355ACDE0D7110205CA2A560D8989D9A560B33E8561B0886BA`。

首次隔离回归在正确密码解除绑定时发现 Npgsql 拒绝预处理带参数的多语句命令。`0.10.1-20260724T102830Z` 将解除绑定、旧身份转移和身份更新事务拆为单语句命令。单文件程序为 `103,634,800` 字节，SHA-256 为 `07452219F072D2CD91E53F427819DC2F13B9E887D278D2F817110F462AC7CBE3`；上传归档为 `45,282,743` 字节，SHA-256 为 `5EAF4651D076B1F72CDFF83ED1D628D046621286C58B6BACC0DB03453FEC36A9`。热修复前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T102852Z.dump` 为 `63,846` 字节，SHA-256 为 `D15397BFB1C318F4141CE97A13AC2A4692C755915FF46BDD9C46C5C6B051D1D4`；配置备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.10.1-20260724T102852Z.tar.gz` 为 `45,426,502` 字节，SHA-256 为 `C5FB969A7A24EBCB69E90F19BF112FAF477D2A1AB68C53B7AEAC3DC589F90CE4`。两轮数据库备份均通过校验和与 `pg_restore --list`。

`0.10.1` 生产回归确认三个启动器会话、管理员会话、后台票据和 Velocity 授权可以原子撤销；错误密码解除绑定返回 403 且不改变状态；正确密码返回 204，并删除身份、回退 `Member` 及撤销全部认证状态。测试账号、会话、身份和审计数据均已精确清理，用户、活动会话与活动授权恢复为 `0`。迁移仍为 `1` 至 `7`，本机与公网健康检查、旧官网和中转 API 均为 200，管理域名仍为 404，公网 8090 不可达，热修复启动后的 journal 无 warning。

注释标签 `api-v0.10.1` 已推送，指向包含热修复源码与生产验收记录的提交 `6083d84`。

`0.11.1` 将已授权对象下载从每账号每分钟 `600` 的固定窗口改为独立令牌桶，并为拒绝响应写入 `Retry-After`。首轮 `0.11.1-20260725T160210Z` 使用容量 `96`、每秒 `40`，真实基础档案续传成功但日志仍出现小文件突发 429；最终 `0.11.1-20260725T165050Z` 调整为容量 `192`、每秒 `80`。登录、管理员、论坛内部策略和全局每 IP 每分钟 `6000` 均未放宽。

最终单文件程序为 `103,711,283` 字节，SHA-256 `0336CBE79E02F2E9F7F7C37490120FAA840CF083C84B02537ACFEA5266B75F45`；上传归档为 `45,309,559` 字节，SHA-256 `E727E9B840E81CDEFE5D45586AEF874B6E082D29562F797F45ECC8C98589E587`。热修复前发布与配置备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.11.1-hotfix-20260725T165050Z.tar.gz` 为 `45,447,113` 字节，SHA-256 `52DA8D1D120D2F3A5983128B66CF22F69C1EBE56754527B04A350CACE8BBECB4`。原子部署后本机与公网健康/就绪均为 200、数据库 ready、旧官网与中转 API 为 200、两个管理员入口保持 404，journal 无 warning。回滚目标为 `0.11.1-20260725T160210Z`，更早的 `0.11.0-20260725T074100Z` 也继续保留。

部署一致性数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260725T170025Z.dump`，大小 `80,137` 字节，SHA-256 `0E32FFDD4AAA0C0306A2950AE2EEE9990921AA26DC87A63EEDD256CC6F0B208`。`sha256sum -c` 通过，`pg_restore --list` 成功读取 `110` 条归档目录项。

`0.12.0` 为 Velocity 首次连接增加授权目标定向：内部授权响应会返回被消费启动授权对应的 `serverId` 与 `velocityTarget`，插件 `0.2.0` 可据此把代理初始大厅目标改写到玩家在启动器中选择的后端服。生产发布 ID 为 `0.12.0-20260725T203001Z`，单文件程序为 `103,716,915` 字节，SHA-256 `B46A22280243BA9801EB66FD628ED598CD27F0FED7995788C4452D222C3B27D1`；发布归档 `artifacts/releases/hechao-api-0.12.0-20260725T203001Z.tar.gz` 为 `45,382,027` 字节，SHA-256 `C76DA133466A4D609F8009A5206FDAFCDDE72DC0CB7D78FBC8E8C8B473DA5D41`。

部署前 API 与配置备份为 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.11.3-20260725T203220Z.tar.gz`，SHA-256 `71D850AABD85AB203CE585C679A53609F91F013DFDAE6937E1B208E88625EC12`。数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260725T203227Z.dump`，SHA-256 `E1E3F1F864D1CB363E426346892DC0C6651409E001DA9F0B05F9435D55A5C7D9`。当前链接指向 `/opt/hechao-launcher-api/releases/0.12.0-20260725T203001Z`，直接回滚目标为 `/opt/hechao-launcher-api/releases/0.11.3-20260725T195000Z`。本机与公网 `/healthz`、`/readyz` 均返回 200，旧官网与中转 API 回归正常。

生产合成授权验证以初始目标 `lobby` 请求已绑定 owner 身份，返回 `Allowed=true`、`ServerId=pvp`、`VelocityTarget=pvp`、`AccessTier=Administrator` 与 `LuckPermsPrimaryGroup=owner`；一次性授权成功消费后，临时授权行已删除并保留运维审计。当前 `Authentication__EnforceCatalogAuthentication=false`，因为 22 个社区账号中只有 1 个完成 Minecraft 绑定。该开关只有在真实四级账号与 Velocity `enforce` 验收完成后才可启用。

`0.13.0` 新增玩家确认后的诊断上传、一次性上传令牌、账号配额、服务端 ZIP
复验、MFA 管理员下载审计和 14 天清理。生产发布 ID 为
`0.13.0-20260726T173536Z`，单文件程序为 `103,796,275` 字节，SHA-256
`F2B7466A9AFAB142F110D7C2EB692DE1BA2FDD653F7CF42D4AE31D5BF7E8C811`；
发布归档为 `45,339,427` 字节，SHA-256
`E7C8DECAFD8A3B47EB63987F8542C8BB034AB86C831F32B242F741FE26ABC728`。
迁移 9、上传成功和错误路径、审计、强制到期清理、公网健康及旧业务均已通过；
真实 TOTP 已于 2026-07-27 登记，编号 `1e707520` 的生产上传、管理员下载、三段
SHA-256 一致性和对应审计均已完成。完整记录见
[`API_RELEASE_0.13.0.md`](API_RELEASE_0.13.0.md) 与
[`DIAGNOSTIC_UPLOAD_OPERATIONS.md`](DIAGNOSTIC_UPLOAD_OPERATIONS.md)。

`0.14.1` 新增服务器公告与一次性开放/关闭时间、玩家搜索、最终访问结果预览及
带原因、到期时间和修订号的单服允许/拒绝规则。迁移 10 只增加服务器排期字段和
访问规则修订字段；目录和 Velocity 授权使用同一排期状态，手动 `Maintenance` /
`Closed` 始终优先。首次 `0.14.0` 切换在路由构建阶段发现 `DELETE` 请求体不能
自动推断，安装脚本在就绪超时前自动恢复 `0.13.0`。`0.14.1` 显式声明请求体，
先在独立端口完成真实配置启动预检，再原子切换生产。完整记录见
[`API_RELEASE_0.14.1.md`](API_RELEASE_0.14.1.md)。

`0.15.0` 增加账号停用/恢复、单设备与全部认证状态撤销、带到期时间和修订号的
Minecraft UUID 封禁，以及目录、对象下载、Minecraft 绑定与 Velocity 的统一拒绝。
迁移 `11` 只新增 UUID 封禁表。最终发布 ID 为
`0.15.0-20260726T202540Z`；生产备份还原后的隔离端到端验收、`261/261` 个
.NET 测试、原子部署、公网回归与部署后无 warning/error 均已通过。完整制品哈希、
备份、接口和回滚边界见 [`API_RELEASE_0.15.0.md`](API_RELEASE_0.15.0.md) 与
[`ADMIN_ACCOUNT_SECURITY_OPERATIONS.md`](ADMIN_ACCOUNT_SECURITY_OPERATIONS.md)。

`0.16.0` 新增论坛既有 Cookie 撤销 outbox、幂等投递 worker、受控四级全局等级命令
和大厅 LuckPerms 代理契约。迁移 `12`、`13` 均为加法变更。最终发布 ID 为
`0.16.0-20260726T222124Z`；`.NET 283/283`、Velocity `11/11`、等级代理 `4/4`、
隔离生产备份还原、管理静态资源、论坛覆盖包构建、生产 worker 投递、原子部署和旧业务
回归均通过。大厅代理文件已部署但没有重启大厅。完整记录见
[`API_RELEASE_0.16.0.md`](API_RELEASE_0.16.0.md) 与
[`LUCKPERMS_TIER_AGENT_OPERATIONS.md`](LUCKPERMS_TIER_AGENT_OPERATIONS.md)。

`0.18.0` 增加启动器安装、修复、回滚、启动和退出的隐私受限遥测，以及管理后台
下载量、失败率、启动器版本和客户端档案版本汇总。迁移 15 为加法表，客户端和服务端
均只保留 30 天；事件 ID 与用户 ID 组成主键，网络重试不会重复计数。候选发布
`0.18.0-20260726T234852Z` 已通过 `314/314` 个 .NET 测试、隔离生产副本验收、
原子生产切换和公网回归。详见
[`API_RELEASE_0.18.0.md`](API_RELEASE_0.18.0.md) 与
[`LAUNCHER_TELEMETRY_OPERATIONS.md`](LAUNCHER_TELEMETRY_OPERATIONS.md)。

`0.19.0` 增加服务器进程、磁盘、TPS、MSPT、GC 和固定探针问题的当前值与 30 天
幂等样本，以及管理后台“服务状态”页。迁移 16 为加法变更，清理任务每 6 小时删除
超过 30 天的样本。最终发布 `0.19.0-20260727T005013Z` 已通过 `325/325` 个 .NET
测试、生产数据库副本隔离验收、无 PDB 候选复验、原子生产切换和公网回归。Windows
采集器 `0.2.0` 已生产运行；Paper/Purpur 代理文件已部署但没有重启游戏服。详见
[`API_RELEASE_0.19.0.md`](API_RELEASE_0.19.0.md) 与
[`SERVER_RUNTIME_METRICS_OPERATIONS.md`](SERVER_RUNTIME_METRICS_OPERATIONS.md)。

`0.20.0` 增加 API 分钟请求指标、活动告警、告警历史、游戏服状态告警、内部合成事件
端点和管理后台“运行告警”页。独立平台监控器 `0.1.2` 每分钟检查公网健康/就绪、
管理入口、私有 OSS 匿名 `403`、旧官网/中转 API、五个 TLS 证书、API 延迟和异地
数据库及平台数据备份状态，并只在告警变化或恢复时发送邮件。最终发布
`0.20.0-20260727T011953Z` 已通过迁移 17、隔离生产副本、原子切换、公网回归和首次
邮件验收。详见 [`API_RELEASE_0.20.0.md`](API_RELEASE_0.20.0.md) 与
[`OPERATIONAL_ALERTS.md`](OPERATIONAL_ALERTS.md)。

`0.20.1` 将私有对象下载改为不记录目标 URL 的受限 HTTPS 重定向结果，并为五个
Nginx server block 启用不含查询字符串和 Referer 的 `hechao_privacy` 访问日志。
真实签名下载回归后 API journal 新增 AccessKey ID/OSS 签名行均为 `0`；合成论坛
重置链接返回 `200`，新访问日志的 token 和敏感查询参数命中均为 `0`。最终发布
`0.20.1-20260727T145451Z` 已完成一致性备份、原子切换、Nginx 配置备份、平滑
reload 和五个公网入口回归。详见
[`API_RELEASE_0.20.1.md`](API_RELEASE_0.20.1.md)。

`0.20.2` 为 Velocity 后续转服增加会话来源档案判定。不同 Minecraft 版本返回
`MinecraftVersionMismatch`，Forge/Fabric/NeoForge 目标档案不一致返回
`ClientProfileMismatch`；同版本 Paper/Vanilla 互转保持兼容。生产发布
`0.20.2-20260727T225819Z` 已通过 `360/360` .NET、`13/13` Velocity Java 和
`8/8` 生产兼容矩阵，详见
[`API_RELEASE_0.20.2.md`](API_RELEASE_0.20.2.md)。

`0.21.0` 增加目标服级协议转换开关和迁移 018。它先在生产数据库备份的独立临时副本
上验证默认关闭、反向目标隔离以及 NeoForge 档案防绕过，随后短期进入生产，成为
`0.22.0` 的实际直接前置版本。2026-07-29 已取消游戏内跨版本回大厅方案，因此生产
从未为大厅开启该开关；迁移 018 只作为 `0.22.0` 的前置结构保留。详见
[`API_RELEASE_0.21.0_CANDIDATE.md`](API_RELEASE_0.21.0_CANDIDATE.md)。

历史隔离证据仍可用
[`manage-protocol-translation-staging.sh`](../deploy/linux/manage-protocol-translation-staging.sh)
复现，但不再是当前生产发布门槛：

```bash
./manage-protocol-translation-staging.sh prepare
./manage-protocol-translation-staging.sh start
./manage-protocol-translation-staging.sh status
./manage-protocol-translation-staging.sh issue-grant
./manage-protocol-translation-staging.sh stop
./manage-protocol-translation-staging.sh remove --confirm-remove
```

该单元只监听 `127.0.0.1:18093`，使用独立数据库和迁移 018；`issue-grant` 只输出
数量与到期时间，不输出玩家身份或 token。2026-07-28 的隔离 Authorizer 认证探针
已得到预期 `PlayerNotLinked`，生产 API 仍是 `0.20.2-20260727T225819Z`、迁移 17。

`0.22.0` 在迁移 019 中增加 `server_role` 与 `monitoring_enabled`。玩家目录、
档案资格、启动授权、Velocity 授权与单服规则只接受 `Player` 角色；Lobby 会自动
迁移为 `Infrastructure`，数据库约束禁止重新设为可见、可授权或玩家目标。心跳、
运行指标、告警和管理员状态改为依赖 `monitoring_enabled`，因此 Lobby 对玩家隐藏后
仍能承载 LuckPerms 等级代理、监控、告警和备份。生产发布通过 `.NET 379/379`、
迁移 `019`、健康/就绪、公开目录零 Lobby、旧业务回归和 journal 零新增错误，见
[`API_RELEASE_0.22.0.md`](API_RELEASE_0.22.0.md)。

API `0.23.x` 在迁移 020 中增加结构化服控目标、操作、命令和代理心跳；`0.24.0`
继续加入每服 `Xms`、`Xmx` 与内存硬上限的展示和受控修改。管理端只能排队启动、
停止、重启、结构化快捷设置和受限 Minecraft 命令；本机代理凭据按代理 ID 存储为
SHA-256，明文令牌只保留在对应 Windows VPS 的 DPAPI `LocalMachine` 密文中。
内存设置不会自动重启游戏服，API 和代理分别执行范围、步长与硬上限校验。完整边界见
[`SERVER_CONTROL_AGENT_OPERATIONS.md`](SERVER_CONTROL_AGENT_OPERATIONS.md)，发布记录见
[`API_RELEASE_0.24.0.md`](API_RELEASE_0.24.0.md)。

管理后台的服控读取已拆为两个合同：`GET /v1/admin/server-control/overview` 只返回
全部目标摘要和活动操作，`GET /v1/admin/server-control/targets/{serverId}` 只返回
当前目标的命令白名单、控制台尾部和最近 20 条操作。前端不得重新把所有目标日志塞回
3 秒概览轮询。

API `0.25.0` 在迁移 021 中增加可撤销可信管理员设备。`0.26.0` 将九个管理模块迁移到
Vue 3、TypeScript、Vite 和 Vue Router，并把服控概览与详情读取真正拆开；该版本曾于
2026-08-02 正式运行，随后发现已消费的票据 fragment 会被 Router 再次写回地址栏。
`0.26.1` 在 Router 创建前清除 fragment 并只消费一次票据，已通过真实启动器票据、
九页稳定数据态、零横向溢出和零浏览器 warning/error 的生产验收。发布记录见
[`API_RELEASE_0.26.1.md`](API_RELEASE_0.26.1.md)。

管理后台环境配置使用 [`configure-admin-web.sh`](../deploy/linux/configure-admin-web.sh)。脚本会备份旧环境文件、创建只允许 `hechao-api` 访问的 Data Protection 目录，并显式写入启用状态，但不会重启 API。

## 2. 本地构建

API 项目会自动构建 `src/Hechao.Api/AdminWeb`，因此构建机还必须具备该工程
`package.json` 约束的 Node.js 与 npm。首次构建或锁文件变化时执行 `npm ci`，
随后执行 TypeScript 检查与 Vite 生产构建；不要使用旧的手工 `admin.js`。

```powershell
dotnet restore src\Hechao.Api\Hechao.Api.csproj -r linux-x64 --source https://api.nuget.org/v3/index.json
dotnet publish src\Hechao.Api\Hechao.Api.csproj `
  -c Release `
  -r linux-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o artifacts\publish\api-linux-x64 `
  --no-restore
```

发布后先计算归档 SHA-256。上传使用临时扩展名，远端哈希一致后才运行 [`install-release.sh`](../deploy/linux/install-release.sh)。

## 3. 部署检查

```bash
systemctl status hechao-launcher-api.service --no-pager
ss -lntp '( sport = :8090 )'
curl -fsS http://127.0.0.1:8090/healthz
curl -fsS http://127.0.0.1:8090/readyz
journalctl -u hechao-launcher-api.service -p warning --since today --no-pager
curl -fsS https://launcher-api.hechao.world/healthz
curl -fsS https://launcher-api.hechao.world/readyz
curl -fsS https://launcher-api.hechao.world/v1/catalog
curl -sS -o /dev/null -w '%{http_code}\n' \
  -H 'Authorization: Bearer invalid' \
  https://launcher-api.hechao.world/v1/catalog
```

过渡阶段预期匿名目录为 200、无效 Bearer 为 401。强制登录启用后匿名目录也必须为 401。

必须同时从外部确认公网 `8090` 不可连接，并分别确认：

```text
https://hechao.world/      -> HTTP 200
https://api.hechao.world/  -> HTTP 200
https://admin.hechao.world/ -> 当前 `AdminWeb__Enabled=true`；Host 锁定与真实 MFA 已验收，管理写入仍按逐项清单执行
```

每次部署还必须确认 `launcher-api.hechao.world/admin/` 不能作为管理入口、管理域名 Host 锁定生效、Data Protection key ring 可写且已加密备份。真实管理员 TOTP 已完成，生产凭据 `2`、恢复码哈希 `16`；Vue 九页只读生产验收已通过，但目录、账号、档案、权限和服控写操作仍须按 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md) 使用专门测试对象逐项验收，并按 [`AUTHENTICATION_OPERATIONS.md`](AUTHENTICATION_OPERATIONS.md) 验证四级真实账号。不得把“九页可用”扩大写成“全部管理写操作已验收”。

### 3.1 日志隐私回归

API 的私有对象下载端点不得使用会记录完整目标地址的通用重定向结果。Nginx
必须从 `deploy/linux/nginx/00-hechao-privacy-log.conf` 加载
`hechao_privacy` 格式，并在论坛/API 与启动器站点共五个 server block 中包含
`/etc/nginx/snippets/hechao-privacy-access-log.conf`。

每次修改后先备份配置并执行：

```bash
nginx -t
systemctl reload nginx
```

用合成值访问 `/forum/reset?token=<synthetic>` 后，新日志必须包含
`GET /forum/reset`，但不得包含合成值、`?token=`、`X-Oss-Signature`、完整
Referer 或 Authorization。不得为了消除旧的、已经失效的短时 URL 而删除历史
journal 或 Nginx 轮转日志。

## 4. 原子回滚

[`install-release.sh`](../deploy/linux/install-release.sh) 会在切换后等待 `/readyz`，失败时自动恢复原符号链接。手动回滚也只切换 `current`，不覆盖已发布版本：

```bash
previous=/opt/hechao-launcher-api/releases/<known-good-release>
test -x "$previous/Hechao.Api"
ln -s "$previous" /opt/hechao-launcher-api/.rollback-next
mv -Tf /opt/hechao-launcher-api/.rollback-next /opt/hechao-launcher-api/current
systemctl restart hechao-launcher-api.service
curl -fsS http://127.0.0.1:8090/healthz
```

若应用版本回滚后仍无法恢复，可将新域名切换到服务器上预置的 ACME-only 配置。该操作仅下线两个新域名，保留证书续期验证路径，不修改旧网站与中转站：

```bash
ln -sfn /etc/nginx/sites-available/hechao-launcher-acme-only.conf \
  /etc/nginx/sites-enabled/hechao-launcher.conf
nginx -t
systemctl reload nginx
```

恢复正式入口时，将符号链接重新指向 `/etc/nginx/sites-available/hechao-launcher.conf`，再次执行 `nginx -t` 后 reload。不得通过修改现有网站或中转站 upstream 来掩盖 API 故障。

## 5. 当前已验证版本

| 发布 ID | 程序 SHA-256 | 状态 |
| --- | --- | --- |
| `0.1.0-20260721T1543Z` | `D02FE8158C7B2AB2A9DC013C433EF887FB0BA71F47E45B1646A3F9D880436F33` | 本机与远端一致，重启后健康检查通过 |
| `0.2.0-20260721T162344Z` | `C8E60B3F80A723352967BF2C4A90357A587403FC97BFA9CD01C7172F17377CF0` | 数据库迁移、就绪检查、目录 API 与公网回归通过 |
| `0.3.0-20260721T171654Z` | `5A1BF5F06F9D7C42337B8D1BF75FA2DBAF1011036BC21FB6ECB83FC4E30FC5BB` | 认证会话、LuckPerms 快照、授权过滤与公网回归通过 |
| `0.4.0-20260723T051123Z` | `975280C2D026F25AF461F0125C0C19AFF18A1357E5FE091937FCA2BBE0A2771C` | 签名清单、受限对象下载、OSS 配置、原子清单发布与公网回归通过 |
| `0.5.0-20260723T102749Z` | `95D2FE3B2E160F205B22B457988D8721970DB580DAD6B1A8A412B1798C42332B` | 一次性启动授权、Velocity 内部判定、迁移 3、权限/公网回归与无警告日志通过 |
| `0.6.0-20260723T123346Z` | `71313BCF82B6B6E1BB095F142E1BA6A06E9ADC7B834FA6F32F9B74914F078780` | 按 Velocity 目标的实时心跳、迁移 4、目录状态合并、任务实测与公网回归通过 |
| `0.9.0-20260723T195253Z` | `159DDBA288078E0F2C6DAA4BF3C3A62507EC3A3F99FBEC24D15A78AAB57ADBBA` | 迁移 5 至 7、赫朝账号完整会话链、无效正版凭据拒绝、测试数据清理与旧域名回归通过；AdminWeb 保持关闭 |
| `0.10.0-20260724T101528Z` | `ECE445F76682775917D089630B6C0105AEE04707EE08D36886E53514E8CDCB11` | 账号安全端点上线；生产回归发现带参数多语句命令与 Npgsql 预处理不兼容，保留为回溯版本 |
| `0.10.1-20260724T102830Z` | `07452219F072D2CD91E53F427819DC2F13B9E887D278D2F817110F462AC7CBE3` | 单语句事务热修复、全部设备退出、解除绑定、精确清理、公网与旧业务回归通过 |
| `0.11.1-20260725T165050Z` | `0336CBE79E02F2E9F7F7C37490120FAA840CF083C84B02537ACFEA5266B75F45` | 论坛统一账号、并行对象签名、独立令牌桶、`Retry-After`、真实基础档案续传与公网回归通过；历史版本 |
| `0.12.0-20260725T203001Z` | `B46A22280243BA9801EB66FD628ED598CD27F0FED7995788C4452D222C3B27D1` | 授权目标定向、PVP 目录与生产合成授权回归通过；历史版本 |
| `0.13.0-20260726T173536Z` | `F2B7466A9AFAB142F110D7C2EB692DE1BA2FDD653F7CF42D4AE31D5BF7E8C811` | 诊断上传、失败路径、审计、14 天清理与旧业务回归通过；历史版本 |
| `0.14.1-20260726T190856Z` | `F02CC7AAC3AE4FC8726548E3777D231D035B03E19487CAB32627333CEBBB8A3A` | 排期、公告、玩家搜索、访问预览、单服规则、迁移 10、启动预检与公网回归通过；历史版本 |
| `0.15.0-20260726T202540Z` | `42ACC44468989A567E936993934046266A9D2B22B43758322E693BC23A089FD6` | 账号与设备会话安全、UUID 封禁、迁移 11、隔离端到端验收、原子部署与公网回归通过；`0.16.0` 的直接回滚目标 |
| `0.16.0-20260726T222124Z` | `8B932BC0BFE5C0D3D2A97460555695AD33544CC2814A96B8AF16E672F8B5CDB5` | 论坛 Cookie 联动、受控 LuckPerms 等级、迁移 12 至 13、生产 worker 投递和公网回归通过；`0.17.0` 的直接回滚目标 |
| `0.17.0-20260726T231515Z` | `80CBE367AE39B46B855DAC31A060E6DC7C50FF80135A4040982429068B674C5B` | 签名发布导入、不可变清单、Test/Gray/Production、稳定分桶、暂停自动回滚、迁移 14、隔离生产副本演练和公网回归通过；`0.18.0` 的直接回滚目标 |
| `0.18.0-20260726T234852Z` | `ED331D29E066AE1363F4A2E8B1D183272821E1E2E97E0ABC9FF27DA03807EB0F` | 隐私受限遥测、幂等批次、30 天留存、运行数据后台、迁移 15、隔离生产副本验收和公网回归通过；`0.19.0` 的直接回滚目标 |
| `0.19.0-20260727T005013Z` | `29B351C33B6366BF2C3E9263275928D0F5C8329D05C14B1C7A138C0D81B279FA` | 进程、磁盘、TPS/MSPT/GC、30 天运行样本、服务状态后台、迁移 16、隔离生产副本验收和公网回归通过；`0.20.0` 的直接回滚目标 |
| `0.20.0-20260727T011953Z` | `67C3E084D9E53509B283A4B39498219C33BF1676BB4F1805A916E83CFFABBDEB` | 请求指标、统一告警、后台告警页、迁移 17、平台监控器、隔离生产副本验收和公网回归通过；`0.20.1` 的直接回滚目标 |
| `0.20.1-20260727T145451Z` | `94BC3831A4749A545968E90BD1ABD638BE26BD23B058091E2A91AF417D09AB54` | 私有签名 URL 不进入 journal，Nginx 查询参数/Referer 脱敏、`355/355` 测试、原子部署和平滑日志切换通过；`0.20.2` 的直接回滚目标 |
| `0.20.2-20260727T225819Z` | `327D17A6F24833CDAD9F912AC16D87EC2DEE463F7DBD427B6E672307DA24A6F6` | 会话来源、Minecraft 版本和模组档案兼容保护，`360/360` .NET、`13/13` Velocity、生产矩阵 `8/8`；历史版本 |
| `0.22.0-20260729T144953Z` | `CCD8EFAF4D1F3F89A1BF7C08F2F407283892F3CC69733155ACA6884D45073A13` | 迁移 018/019、玩家/基础设施角色、隐藏后监控、Lobby 永久不可授权，`.NET 379/379`；历史版本 |
| `0.25.0-20260801T105011Z` | `B69D32F1A374BE2BF96E875931861B7F09F786348B7C7AD7A1F26883559E2E9A` | 迁移 021、可信管理员设备、真实 MFA 与第二次启动器票据免动态码通过；历史版本 |
| `0.26.0-20260802T010000Z` | `40E2B24EC1D2AD1E61156430AE2D522EB662061A00DB2F3EEEDB9E19911F0204` | Vue 九页首次生产版本；因票据 fragment 在 Router 启动后回写而被 `0.26.1` 热修替代；历史版本 |
| `0.26.1-20260802T012527Z` | `61D8E11F556FC215E52DE0295B106CC9C309F8CAB81ED283A5EE249B86C09DDF` | Vue 九页、真实票据预路由清理、完整备份、原子切换、生产稳定数据态和公网回归通过；`0.26.2` 的直接回滚目标 |
| `0.26.2-20260802T093332Z` | `38C9A7C8F09FAE7E871E815808EDB4F50C0AA108CD5D707DA5F067B6DB45DAA2` | 长正文、短窗口与侧栏滚动边界修复，`12/12` Playwright、完整备份、原子切换和公网回归通过；`0.27.0` 的灾难恢复基线，已有 `DeployPackage` 记录后不可单独回退二进制 |
| `0.27.0-20260803T174833Z` | `14FC6D22338A368B26556FAF108A814A0B1C3CB20C03791FF9E7356DC7D58AD8` | 迁移 022/023、Vue 第十页、整合包续传识别、Publisher 编排、Test-only 发布和固定活动槽部署已上线；固定试包、原活动目录恢复、内外网健康与数据库就绪通过；`0.27.1` 的直接回滚目标 |
| `0.27.1-20260804T211905Z` | `F68790888A1DBFF6AC8C973F530E28697B6A901A98458179C4D7D37C8DE2D796` | 增加严格脱敏的公开活动投影、公开启动器元数据和短期下载重定向；无迁移，官网日程与下载页已接入；历史版本 |
| `0.28.0-20260805T201046Z` | `87DC3054EB2C91FA6E7060A3521588AA25A3E8182E25542F4919110D33838108` | 迁移 024、管理后台受控删除服务端文件、代理能力与目录状态故障关闭、完整备份和原子切换均已上线；`0.28.1` 的直接回滚目标。产生 `DeleteServerFiles` 记录后不得继续回滚到 `0.27.3` |
| `0.28.1-20260805T214936Z` | `C3771C9CF816D1BA091362FF7D67E76C63740EA19E9F29FBFCEFE43A15C583D3` | 已删除、已清理且无活动操作的目标从服控概览隐藏，重新部署后自动恢复；完整备份、原子切换、公网回归和生产数据条件核验通过；`0.28.2` 的直接回滚目标 |
| `0.28.2-20260805T222544Z` | `94551BDF1296DFD7FB513004D10461C321619D24CA042605D607DB60E46F7DB7` | 整合包精确确认文本纳入草稿快照，3 秒轮询不再清空输入；Playwright `16/16`、完整备份、原子切换和公网回归通过；`0.28.3` 的直接回滚目标 |
| `0.28.3-20260805T234331Z` | `EBF43D83FD3D883464180C227D8B64701FC3FD851FBCFB48CF138EF86185DFB4` | 整合包页可读取已删除目录但仍可重新部署的固定活动槽，普通服控列表继续隐藏已删除目标；生产 9 个总目标、普通概览 6 个、整合包概览 9 个。`0.28.4` 的直接回滚目标 |
| `0.28.4-20260806T002900Z` | `BECAAF0660EC5E56C2DD26A2A0D52AE5417B635F4369A2BF70C577CD3917DD8D` | 已删除固定活动槽在 `settings=null` 时使用受控 `4096 MiB` 部署上限，页面、确认接口和编排保持一致。`0.28.5` 的直接回滚目标 |
| `0.28.5-20260806T125215Z` | `D24FDBC352E2485FF8C5992F21CA4074B26E4C77CD4DAFF68EF379D7647F4C22` | 迁移 025 保存 VPS 真实物理内存；整合包页显示总内存和推荐最小/最大值，推荐区间不禁用提交；移除 `4096 MiB` 回退上限。`0.28.6` 的直接回滚目标 |
| `0.28.6-20260806T150509Z` | `974A67212B477F0E37CE435CAD6C3369D6C4C5F791BE8A1009924CA1186786C3` | 迁移 026 保存 Publisher 结构化进度；生产真实任务已覆盖下载、解压、对象发布与最终化，并显示对象数、字节数和 ETA。`0.28.7` 的直接回滚目标 |
| `0.28.7-20260807T072043Z` | `09A30BA02CECF80E978B523D51E9596510373C829075E1F6C0923FF650790AE9` | 无迁移；修复服务器新增/编辑和单服权限等表单型抽屉被压缩到顶部的问题。Playwright `18/18`、完整备份、原子切换、公网回归通过；当前线上版本 |

数据库、真实目录与 LuckPerms 链路已于 2026-07-22 完成，Velocity 授权 API 与服务器心跳已于 2026-07-23 完成，赫朝账号、账号安全、论坛统一账号与 Cookie 联动、受控全局等级、授权定向路由、诊断上传、服务器排期、单服规则、三通道客户端发布、隐私受限遥测、服务器进程/磁盘运行指标、统一告警、生产日志脱敏、客户端兼容保护和 Vue 管理后台均已部署。API `0.28.7`、启动器 `0.14.2`、Publisher Agent `1.2.1`、owl5 ServerControlAgent `0.4.2`、owl9 ServerControlAgent `0.4.0`、Authorizer `0.4.0` 和 Lobby Guard `0.1.0` 组成当前启动器唯一切服生产基线。真实管理员 MFA、可信设备、Vue 十页、固定整合包 Test-only 发布、停止活动槽部署、原活动目录恢复、官网活动投影、启动器下载桥接和白名单服务端文件删除均已验收；整合包精确确认输入在后台轮询期间保持，生产已有三个成功删除记录，其目标在清理完成后从日常服控列表隐藏，但固定活动槽仍可由整合包页重新部署。整合包页当前显示 owl5 VPS 总内存 `18431 MiB` 和 `4096-8960 MiB` 推荐区间，区间外合法值仍可提交；Publisher 进度已覆盖对象数、字节数和 ETA。后台表单型抽屉已随 `0.28.7` 修复顶部压缩问题。五服指标代理已经加载，Activity 单账号路由、特殊路径物理原生库加载和安装版真实进服已通过，恐怖整蛊服务端兼容修复已完成历史真实验收；四级真实账号、真实玩法包和多人灰度仍未完成外部验收。认证激活步骤见 [`AUTHENTICATION_OPERATIONS.md`](AUTHENTICATION_OPERATIONS.md)，管理员后台见 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md)，整合包导入见 [`PACKAGE_IMPORT_OPERATIONS.md`](PACKAGE_IMPORT_OPERATIONS.md)，服控与删除边界见 [`SERVER_CONTROL_AGENT_OPERATIONS.md`](SERVER_CONTROL_AGENT_OPERATIONS.md)，Velocity 灰度与强制顺序见 [`VELOCITY_AUTHORIZATION_OPERATIONS.md`](VELOCITY_AUTHORIZATION_OPERATIONS.md)，心跳见 [`SERVER_HEARTBEAT_OPERATIONS.md`](SERVER_HEARTBEAT_OPERATIONS.md)，深度指标见 [`SERVER_RUNTIME_METRICS_OPERATIONS.md`](SERVER_RUNTIME_METRICS_OPERATIONS.md)，统一告警见 [`OPERATIONAL_ALERTS.md`](OPERATIONAL_ALERTS.md)，数据库运维见 [`DATABASE_OPERATIONS.md`](DATABASE_OPERATIONS.md)。

# 启动器 API 运维与回滚

> 当前线上版本：`0.20.0-20260727T011953Z`
> 本地 API 源码版本：`0.20.0`
> 当前阶段：`0.20.0` 统一运行告警已生产部署；启动器 `0.11.13` 已完成私有 OSS 灰度发布，管理员 Web 已启用但尚未登记 MFA

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
- 管理员浏览器与目录端点：`/v1/admin-auth/*`、`/v1/admin/*`，仅允许管理域名上的独立 Cookie 会话；目录写入还要求 MFA 与 CSRF
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
生产管理员 MFA 下载仍待真实 TOTP 登记后验收。完整记录见
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
端点和管理后台“运行告警”页。独立平台监控器 `0.1.0` 每分钟检查公网健康/就绪、
管理入口、私有 OSS 匿名 `403`、旧官网/中转 API、五个 TLS 证书、API 延迟和异地
数据库备份状态，并只在告警变化或恢复时发送邮件。最终发布
`0.20.0-20260727T011953Z` 已通过迁移 17、隔离生产副本、原子切换、公网回归和首次
邮件验收。详见 [`API_RELEASE_0.20.0.md`](API_RELEASE_0.20.0.md) 与
[`OPERATIONAL_ALERTS.md`](OPERATIONAL_ALERTS.md)。

管理后台环境配置使用 [`configure-admin-web.sh`](../deploy/linux/configure-admin-web.sh)。脚本会备份旧环境文件、创建只允许 `hechao-api` 访问的 Data Protection 目录，并显式写入启用状态，但不会重启 API。

## 2. 本地构建

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
https://admin.hechao.world/ -> 当前 `AdminWeb__Enabled=true`；必须确认 Host 锁定与登录页正常，MFA 登记前不得执行管理写入
```

每次部署还必须确认 `launcher-api.hechao.world/admin/` 不能作为管理入口、管理域名 Host 锁定生效、Data Protection key ring 可写且已加密备份。随后按 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md) 完成真实管理员 TOTP 和审计验收，并按 [`AUTHENTICATION_OPERATIONS.md`](AUTHENTICATION_OPERATIONS.md) 验证赫朝账号与旧身份接管。当前没有 MFA 凭据，不得把“AdminWeb 已启用”写成“管理后台已完成安全验收”。

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
| `0.20.0-20260727T011953Z` | `67C3E084D9E53509B283A4B39498219C33BF1676BB4F1805A916E83CFFABBDEB` | 请求指标、统一告警、后台告警页、迁移 17、平台监控器、隔离生产副本验收和公网回归通过；当前线上版本 |

数据库、真实目录与 LuckPerms 链路已于 2026-07-22 完成，Velocity 授权 API 与服务器心跳已于 2026-07-23 完成，赫朝账号、账号安全、论坛统一账号与 Cookie 联动、受控全局等级、授权定向路由、诊断上传、服务器排期、单服规则、三通道客户端发布、隐私受限遥测、服务器进程/磁盘运行指标和统一告警已部署。API `0.20.0` 为当前线上版本，启动器 `0.11.13` 已完成私有 OSS 灰度发布；管理员 Web 已启用但尚未登记 MFA，大厅等级代理与三个 Paper/Purpur 指标代理等待下次手动重启后加载。认证激活步骤见 [`AUTHENTICATION_OPERATIONS.md`](AUTHENTICATION_OPERATIONS.md)，管理员后台见 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md)，Velocity 灰度与强制顺序见 [`VELOCITY_AUTHORIZATION_OPERATIONS.md`](VELOCITY_AUTHORIZATION_OPERATIONS.md)，心跳见 [`SERVER_HEARTBEAT_OPERATIONS.md`](SERVER_HEARTBEAT_OPERATIONS.md)，深度指标见 [`SERVER_RUNTIME_METRICS_OPERATIONS.md`](SERVER_RUNTIME_METRICS_OPERATIONS.md)，统一告警见 [`OPERATIONAL_ALERTS.md`](OPERATIONAL_ALERTS.md)，数据库运维见 [`DATABASE_OPERATIONS.md`](DATABASE_OPERATIONS.md)。

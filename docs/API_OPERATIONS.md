# 启动器 API 运维与回滚

> 当前线上版本：`0.6.0`
> 本地源码版本：`0.9.0`，尚未部署
> 当前阶段：线上 `0.6.0` 保持不变；源码 `0.9.0` 在 `0.8.0` 管理控制台基础上增加赫朝账号、密码哈希和独立 Minecraft 身份绑定

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
- 启动器进服授权：`POST /v1/velocity/launch-grants`
- Velocity 内部授权：`POST /v1/internal/velocity/authorize`
- LuckPerms 内部端点：`POST /v1/internal/luckperms/snapshot`
- 服务器心跳内部端点：`POST /v1/internal/server-heartbeats`
- 管理员票据端点：`POST /v1/admin-auth/tickets`，仅允许 `Administrator` 启动器会话
- 管理员浏览器与目录端点：`/v1/admin-auth/*`、`/v1/admin/*`，仅允许管理域名上的独立 Cookie 会话；目录写入还要求 MFA 与 CSRF
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

`0.7.0` 源码新增管理员服务器目录 CRUD、归档/恢复、乐观并发修订号和事务内审计日志。迁移 5 只增加 `servers.revision` 与审计目标索引；回滚到 `0.6.0` 时可以保留这两个兼容字段。详细接口、验证和回滚边界见 [`ADMIN_CATALOG_OPERATIONS.md`](ADMIN_CATALOG_OPERATIONS.md)。本段只记录源码状态，不表示已部署。

`0.8.0` 源码新增启动器管理员入口、90 秒一次性票据、来源 IP 绑定、独立 `HttpOnly` 浏览器会话、TOTP 与恢复码、防 TOTP 重放、CSRF、管理域名锁定和静态 Web 控制台。迁移 6 只新增后台票据、会话和 MFA 表；详细密钥、Nginx、部署与回滚边界见 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md)。本段只记录源码状态，不表示已部署。

`0.9.0` 源码新增赫朝账号注册与登录、PBKDF2 密码哈希、账号/邮箱唯一索引、独立 Minecraft 正版身份绑定和旧 `legacy_*` 身份安全接管。迁移 7 为 `launcher.users` 增加 `username`、`email` 与 `password_hash`；回滚到旧 API 时可以保留这些兼容字段，但新建赫朝账号无法使用旧客户端登录。部署顺序必须是数据库备份、API `0.9.0` 灰度、注册/登录/绑定验收，最后才分发启动器 `0.8.0`。

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
https://admin.hechao.world/ -> 当前生产基线 HTTP 404；部署 0.8.0 后 /admin/ 应为 200
```

部署 `0.9.0` 时还必须确认 `launcher-api.hechao.world/admin/` 继续返回 404、管理域名 Host 锁定生效、Data Protection key ring 可写且已加密备份。随后按 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md) 完成真实管理员 TOTP 和审计验收，并按 [`AUTHENTICATION_OPERATIONS.md`](AUTHENTICATION_OPERATIONS.md) 验证赫朝账号与旧身份接管。

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

数据库、真实目录与 LuckPerms 链路已于 2026-07-22 完成，Velocity 授权 API 与服务器心跳已于 2026-07-23 完成。`0.7.0`、`0.8.0` 与 `0.9.0` 尚无生产发布 ID。认证激活步骤见 [`AUTHENTICATION_OPERATIONS.md`](AUTHENTICATION_OPERATIONS.md)，管理员后台见 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md)，Velocity 灰度与强制顺序见 [`VELOCITY_AUTHORIZATION_OPERATIONS.md`](VELOCITY_AUTHORIZATION_OPERATIONS.md)，心跳见 [`SERVER_HEARTBEAT_OPERATIONS.md`](SERVER_HEARTBEAT_OPERATIONS.md)，数据库运维见 [`DATABASE_OPERATIONS.md`](DATABASE_OPERATIONS.md)。

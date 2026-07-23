# 管理员 Web 控制台与 MFA

> 源码版本：启动器 `0.8.0`、API `0.9.0`
> 生产状态：尚未部署，线上仍为 API `0.6.0`
> 管理入口：`https://admin.hechao.world/admin/`
> 运行边界：只管理平台目录数据，不控制 Minecraft、Velocity 或 Java 进程

## 1. 登录链路

1. 玩家先在启动器完成 Microsoft/Minecraft 正版登录。
2. API 每次创建票据时重新确认当前 LuckPerms 映射等级为 `Administrator`。
3. 启动器调用 `POST /v1/admin-auth/tickets`，取得 90 秒、一次性票据。
4. 启动器用系统浏览器打开 `/admin/#ticket=<token>`。fragment 不会随 HTTP 请求、访问日志或 `Referer` 发送。
5. 页面从地址栏移除 fragment，再以 JSON 将票据提交到 `POST /v1/admin-auth/redeem`。
6. API 校验票据哈希、过期时间、一次性状态、管理员状态和来源 IP，随后创建独立浏览器会话。
7. 浏览器只保存 `__Host-HechaoAdmin` Cookie；启动器 Bearer 不进入网页、localStorage、sessionStorage 或 URL 查询参数。

同一管理员最多保留 5 个未撤销会话。默认会话时长 30 分钟。账号被禁用或不再是 `Administrator` 后，下一次请求立即失效。

## 2. 双重验证

- 首次进入后台必须创建 TOTP 密钥，并用验证器扫描二维码或手工输入密钥。
- TOTP 使用 SHA-1、30 秒周期和 6 位数字，允许前后各一个时间窗口。
- 同一个 TOTP 时间窗口只能成功一次，重复提交会被拒绝。
- 启用时生成 8 个高熵恢复码，仅在该次响应中显示；数据库只保存 SHA-256。
- 恢复码使用后立即从数据库事务中删除，不能重复使用。
- 每个新浏览器会话都必须重新完成 TOTP 或恢复码验证。

恢复码必须离线保存。当前版本没有远程关闭 MFA 或绕过 MFA 的接口；若验证器和全部恢复码同时丢失，应暂停部署并设计带双人复核和审计的恢复流程，不要直接删除生产凭据记录。

## 3. 浏览器安全边界

- 会话 Cookie：`HttpOnly`、`Secure`、`SameSite=Strict`、`Path=/`，不设置 `Domain`。
- CSRF：所有管理写请求和 MFA 写请求必须携带 `X-CSRF-TOKEN`。
- 主机锁定：页面、票据兑换、浏览器会话和 `/v1/admin/*` 只接受配置的 `admin.hechao.world` Host。
- CSP：脚本、样式、连接和普通图片只允许同源；TOTP 二维码额外允许 `data:` 图片。
- 响应统一使用 `Cache-Control: no-store`、`X-Frame-Options: DENY`、`Referrer-Policy: no-referrer` 和 `X-Content-Type-Options: nosniff`。
- MFA 尝试按来源 IP 限制为 5 分钟 10 次，票据创建和兑换按来源 IP 限制为每分钟 10 次。
- TOTP 密钥使用 ASP.NET Core Data Protection 加密后写入 PostgreSQL；Data Protection key ring 不得放进 Git 或发布目录。

## 4. 配置

使用无秘密配置脚本写入环境文件、创建 key ring 目录并备份旧配置。脚本默认保持关闭，而且不会重启 API：

```bash
sudo bash deploy/linux/configure-admin-web.sh false
```

完成发布物、数据库、Nginx 和备份检查后，正式启用时显式传入 `true`，再走标准 API 发布流程：

```bash
sudo bash deploy/linux/configure-admin-web.sh true
```

`/etc/hechao-launcher-api/environment` 将包含：

```text
AdminWeb__Enabled=true
AdminWeb__PublicBaseUrl=https://admin.hechao.world
AdminWeb__DataProtectionKeyPath=/var/lib/hechao-launcher-api/data-protection
AdminWeb__TicketSeconds=90
AdminWeb__SessionMinutes=30
AdminWeb__EnrollmentMinutes=10
AdminWeb__TotpIssuer=Hechao
```

脚本等价的 key ring 目录创建命令为：

```bash
install -d -m 700 -o hechao-api -g hechao-api \
  /var/lib/hechao-launcher-api/data-protection
```

key ring 必须纳入独立加密备份。丢失或被替换后，现有 TOTP 密钥无法解密，管理员会被锁在后台外；复制 key ring 也等于复制解密能力，因此备份访问权限必须比普通发布物更严格。

systemd 单元必须保留 `ReadWritePaths=-/var/lib/hechao-launcher-api/data-protection`，否则 `ProtectSystem=strict` 会阻止 API 写入 key ring。前导 `-` 只允许首次准备配置前目录暂不存在；正式启用时目录仍必须由脚本以 `0700` 创建。脚本与单元模板都不执行 `systemctl restart`。

## 5. Nginx 边界

`admin.hechao.world` 应使用现有 Nginx 和现有证书，代理到同一个回环 API。必须传递原始 Host、协议和客户端地址：

```nginx
location / {
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_pass http://127.0.0.1:8090;
}
```

不要把管理后台改成第二个公网 Kestrel 监听，也不要把 Data Protection key、数据库凭据或启动器令牌写进 Nginx 配置。

## 6. 部署检查

正式部署前：

1. 生成 API `0.9.0` 与启动器 `0.8.0` 发布物并核对 SHA-256。
2. 备份 PostgreSQL，确认 `pg_restore --list` 可读。
3. 备份 API 环境文件和 Nginx 站点。
4. 创建并备份 Data Protection key ring。
5. 部署 API 后确认迁移 5、迁移 6、`healthz` 和 `readyz`。
6. 验证 `launcher-api.hechao.world/admin/` 返回 404，`admin.hechao.world/admin/` 返回控制台。
7. 用真实管理员从启动器打开后台，完成首次 TOTP 与恢复码保存。
8. 用普通成员确认票据端点返回 403。
9. 创建一条隐藏测试服务器，编辑、归档、恢复，并核对修订冲突与审计记录。
10. 回归旧网站、中转 API、玩家目录、下载、心跳和 Velocity 授权。

本次源码开发没有执行以上生产部署，也没有重启任何 Minecraft 或 Velocity 进程。

## 7. 回滚

应用故障时可把 API `current` 链接切回 `0.6.0`。迁移 5 和迁移 6 都是加法变更，旧 API 不读取新增表与字段，回滚时不要删除 MFA、会话或审计记录。

若只需关闭管理后台，先将 `AdminWeb__Enabled=false`，再按 API 标准发布流程重启 `hechao-launcher-api.service`。这只影响后台，不要求也不允许重启大厅、生存服、活动服、Velocity 或其他 Minecraft 服务。

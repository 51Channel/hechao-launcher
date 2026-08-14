# 管理员 Web 控制台与 MFA

> 当前生产：API `0.30.6`，`AdminWeb__Enabled=true`
> 生产状态：真实管理员已完成 MFA 和可信设备验收，十一页 Vue 后台与活动企划月历均已部署
> 管理入口：`https://admin.hechao.world/admin/`
> 前端状态：Vue 3、TypeScript、Vite 与 Vue Router；第十一页“活动企划”已完成生产点击验收
> 运行边界：服控只能通过独立最小权限代理执行结构化动作，网页不能取得 PowerShell、CMD、SSH 或任意进程权限

## 1. 登录链路

1. 玩家先在启动器完成 Microsoft/Minecraft 正版登录。
2. API 每次创建票据时重新确认当前 LuckPerms 映射等级为 `Administrator`。
3. 启动器调用 `POST /v1/admin-auth/tickets`，取得 90 秒、一次性票据。
4. 启动器用系统浏览器打开 `/admin/#ticket=<token>`。fragment 不会随 HTTP 请求、访问日志或 `Referer` 发送。
5. Vue 入口在创建 Router 前从地址栏移除 fragment，并且只消费一次票据，再以 JSON 将其提交到 `POST /v1/admin-auth/redeem`。
6. API 校验票据哈希、过期时间、一次性状态、管理员状态和来源 IP，随后创建独立浏览器会话。
7. 浏览器保存短期 `__Host-HechaoAdmin` Cookie；管理员显式启用本机信任后，另保存 `__Host-HechaoAdminTrusted`。启动器 Bearer 不进入网页、localStorage、sessionStorage 或 URL 查询参数。

同一管理员最多保留 5 个未撤销会话。默认会话时长 30 分钟。账号被禁用或不再是 `Administrator` 后，下一次请求立即失效。

## 2. 双重验证

- 首次进入后台必须创建 TOTP 密钥，并用验证器扫描二维码或手工输入密钥。
- TOTP 使用 SHA-1、30 秒周期和 6 位数字，允许前后各一个时间窗口。
- 同一个 TOTP 时间窗口只能成功一次，重复提交会被拒绝。
- 启用时生成 8 个高熵恢复码，仅在该次响应中显示；数据库只保存 SHA-256。
- 恢复码使用后立即从数据库事务中删除，不能重复使用。
- 未启用本机信任的每个新浏览器会话都必须重新完成 TOTP 或恢复码验证。

恢复码必须离线保存。本机信任不能关闭或重置 MFA，也不能替代启动器管理员身份；若验证器和全部恢复码同时丢失，应暂停部署并设计带双人复核和审计的恢复流程，不要直接删除生产凭据记录。

### 2.1 受信任的本机

- 只有已经通过 MFA 的管理员浏览器会话，才能通过复选框或侧栏盾牌按钮信任当前浏览器配置文件；默认有效期为 30 天，服务端配置上限为 90 天。
- 可信令牌是 256 位随机值，只进入 `HttpOnly`、`Secure`、`SameSite=Strict`、Host-only Cookie；PostgreSQL 只保存 SHA-256，不保存明文。
- 受信任设备仍必须由已登录启动器创建 90 秒一次性管理员票据。可信 Cookie 只能把同一管理员的新后台会话标记为已完成 MFA，不能单独创建管理员身份或通过直接 URL 登录。
- 每个管理员最多保留 3 个有效可信设备。账号停用、密码修改、撤销全部会话或显式退出后台都会撤销相应可信设备；无痕窗口、其他浏览器配置文件和其他电脑仍要求 MFA。
- 创建、使用和撤销分别写入 `admin.trusted_device.created`、`admin.trusted_device.used` 和 `admin.trusted_device.revoked` 审计事件，审计数据不包含令牌。

## 3. 浏览器安全边界

- 会话 Cookie：`HttpOnly`、`Secure`、`SameSite=Strict`、`Path=/`，不设置 `Domain`。
- CSRF：所有管理写请求和 MFA 写请求必须携带 `X-CSRF-TOKEN`。
- 主机锁定：页面、票据兑换、浏览器会话和 `/v1/admin/*` 只接受配置的 `admin.hechao.world` Host。
- CSP：脚本、样式、连接和普通图片只允许同源；TOTP 二维码额外允许 `data:` 图片。
  FullCalendar 使用内容固定的 `data-fullcalendar` 样式锚点及精确 SHA-256 CSP source，
  不得改成 `'unsafe-inline'`；修改锚点文本后必须同步哈希并运行生产 CSP 路由回归。
- 响应统一使用 `Cache-Control: no-store`、`X-Frame-Options: DENY`、`Referrer-Policy: no-referrer` 和 `X-Content-Type-Options: nosniff`。
- MFA 尝试按来源 IP 限制为 5 分钟 10 次，票据创建和兑换按来源 IP 限制为每分钟 10 次。
- TOTP 密钥使用 ASP.NET Core Data Protection 加密后写入 PostgreSQL；Data Protection key ring 不得放进 Git 或发布目录。

### 3.1 Vue 前端工程

管理后台源码位于 `src/Hechao.Api/AdminWeb`，使用 Vue 3、TypeScript、Vite 和
Vue Router。源码候选的十一个页面分别对应 `/admin/servers`、`users`、`profiles`、
`package-imports`、`activity-plans`、`telemetry`、`runtime`、`control`、`alerts`、
`diagnostics` 和 `audit`。
ASP.NET Core 使用 `/admin/{*path:nonfile}` 回退到 Vue 入口，因此刷新深层路由仍由
同一应用接管。

生产构建输出到 `src/Hechao.Api/wwwroot/admin`。源码映射已关闭，生成的 JavaScript、
分块脚本和 CSS 不得手工修改；应修改 `AdminWeb/src` 后重新构建。`.csproj` 会在
`dotnet build` 和 `dotnet publish` 前执行 `npm ci` 与 `npm run build`，发布目录仍只
包含构建后的静态资源，不包含 `node_modules`、测试结果或前端源码。

独立验证命令必须在 PowerShell 7 中执行：

```powershell
Set-Location src\Hechao.Api\AdminWeb
npm ci
npm run typecheck
npm test
npm run test:e2e
npm run build
```

Playwright 覆盖十一个路由的真实数据形态、移动端横向溢出、WCAG A/AA 自动检查、
服控轮询期间的脏表单和控制台阅读位置、正式通道确认、修订冲突恢复，以及生产 CSP
下从整合包页进入 FullCalendar。API 测试另验证静态资源 MIME、构建后样式锚点及哈希、
`/admin/control` 深层路由、Host 锁定和安全响应头。

### 3.2 整合包导入

`/admin/package-imports` 只对完成 MFA 的 `Administrator` 开放。页面提供分块上传、暂停、
续传、取消、识别结果审阅和任务时间线。默认“仅发布并入库”不要求活动槽在线或停服，
只发布客户端并保留服务端制品；兼容的“立即部署活动槽”才要求具备整合包能力、代理在线
且当前停止的 owl5 活动目标。客户端发布阶段显示 Publisher 上报的真实对象/字节进度，并在
取得两个有效样本后估算剩余时间；解压和构建等无法量化的阶段使用不确定进度，不显示
伪造百分比。客户端只发布到 `Test`；服务端无论立即部署还是稍后从企划页部署，完成后
都保持停止。

页面不会提供强制忽略阻断项、选择其他 VPS、自动关冲突服、自动开服或直接推进
`Production` 的入口。API、Publisher、服控代理配置与回滚见
[`PACKAGE_IMPORT_OPERATIONS.md`](PACKAGE_IMPORT_OPERATIONS.md)。
正式前端已通过 TypeScript、Vitest `8/8`、Playwright `14/14`、十路由 WCAG A/AA、
桌面与 390px 移动端溢出检查；Impeccable 检测为零项。生产固定试包完成上传、识别、
人工确认、任务时间线、Test-only 发布和停止活动槽部署；原活动目录随后
恢复，页面没有自动开服或推进正式通道。

### 3.3 活动企划

`/admin/activity-plans` 使用 FullCalendar 管理同一套 Launcher 活动企划。点击日期或框选
范围创建草稿，拖动改变整个区间，拖动两端改变开始和结束。草稿允许重叠；任意时刻
最多一个已发布活动，区间按 `[开始, 结束)` 计算，因此相邻活动可以无缝接档。

生产 API `0.30.6` 起，页面还会查询 `velocityTarget=activity`、已有开放或结束时间、但尚未
建立 `activity_plan_status` 的旧服务器目录排期。它们以蓝色虚线事件和独立告警清单
只读显示，点击后列出缺少的时间边界与整合包绑定，并提供服务器目录和整合包导入入口。
旧排期不计入草稿/已发布统计，也不能拖动、发布、部署或触发服控命令；转换必须由
管理员补齐结束时间、确认已完成整合包后走正式企划创建与审核流程。

该能力已随 `0.30.6-20260814T133415Z` 上线。生产数据库当前命中一条
`activity / 赫朝商务追杀` 旧排期；后台静态资源、内外网健康、零正式企划和零活动任务
均已核对，发布未提交任何 Minecraft 服控命令。

发布要求绑定整合包的精确清单已进入 `Production`。玩家发布后即可提前下载客户端，
但只有当前时间进入排期、活动槽在线且代理上报的部署 import 与企划绑定完全一致时才
允许进入。部署使用 `DEPLOY <planId>` 精确确认，成功后仍保持停服。页面每 5 秒刷新，
并通过修订号防止与官网后台互相覆盖。完整操作与双端桥接见
[`ACTIVITY_PLAN_OPERATIONS.md`](ACTIVITY_PLAN_OPERATIONS.md)。

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
AdminWeb__TrustedDeviceDays=30
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

1. 生成目标 API 与启动器安装包并核对版本、提交号和 SHA-256；当前 API 基线为 `0.26.1-20260802T012527Z`，启动器为 `0.14.2`。
2. 备份 PostgreSQL，确认 `pg_restore --list` 可读。
3. 备份 API 环境文件和 Nginx 站点。
4. 创建并备份 Data Protection key ring。
5. 部署当前候选 API 后确认迁移 5、迁移 6、迁移 10、迁移 11、迁移 15、迁移 16、迁移 19、迁移 20、迁移 21、迁移 28、`healthz` 和 `readyz`。
6. 验证 `launcher-api.hechao.world/admin/` 返回 404，`admin.hechao.world/admin/` 返回控制台。
7. 验证 `/admin/servers`、`/admin/activity-plans`、`/admin/control` 和 `/admin/audit` 直接刷新均返回 Vue 入口，浏览器控制台没有资源 404。
8. 用真实管理员从启动器打开后台，完成首次 TOTP 与恢复码保存；显式信任当前电脑后再次从启动器打开后台，确认不再要求动态码。
9. 用普通成员确认票据端点返回 403。
10. 创建一条隐藏测试服务器，编辑、归档、恢复，并核对修订冲突与审计记录。
11. 验证玩家账号停用/恢复、设备会话撤销、UUID 封禁/解除、最后管理员保护和审计。
12. 创建两条重叠草稿，确认第一条可发布、第二条返回冲突；再验证首尾相接的两条企划可发布。检查拖动、两端 resize、官网后台同步和审计后清理测试企划。
13. 回归旧网站、中转 API、玩家目录、下载、心跳和 Velocity 授权。

API `0.20.0` 已生产部署，管理后台开关已启用。2026-07-27 的真实登记由管理员
在启动器生成的一次性浏览器会话中完成；生产数据库核对结果为 MFA 凭据 `1`、
恢复码哈希 `8`、有效登记 `0`、有效且已完成 MFA 的后台会话 `1`。审计记录包含
`admin.mfa.enrollment.started` 与 `admin.mfa.enabled`。恢复码正文、TOTP 密钥、
Cookie、来源地址和用户标识均未进入仓库证据。

2026-07-26 的生产只读复核已确认 `AdminWeb__Enabled=true`，
`https://admin.hechao.world/admin/` 返回 200，并带有 `no-store`、CSP、
`DENY` frame policy 等安全响应头。首次 MFA 安全链已经验收；目录写入、账号安全、
发布工作台、运行数据、服务状态和运行告警仍应按本节清单逐页执行真实生产验收，
不能仅凭 MFA 会话存在便整体标记完成。

2026-07-30 又执行了生产控制面只读验收。API 进程和发布目录在验收前后保持不变，
数据库迁移为 `19/19`，管理站八个页面模块及其脚本均已部署，匿名管理请求为 `401`，
错误 Host 的 `/admin/` 为 `404`，六个生产档案、实时指标、告警、工作队列和所需审计
均通过，共 `32` 项检查且技术失败为 `0`。本轮同时修复了 Nginx 与 API 重复发送安全
响应头的问题：Nginx 继续对所有响应添加单份安全头，并使用 `proxy_hide_header` 隐藏
上游同名值；配置 reload 前后均通过 `nginx -t`，API PID 未变化。机器证据见
[`evidence/PRODUCTION_CONTROL_PLANE_READINESS_2026-07-30.json`](evidence/PRODUCTION_CONTROL_PLANE_READINESS_2026-07-30.json)。

截至 2026-08-02，生产 MFA 凭据为 `2`、恢复码哈希为 `16`。2026-07-30 10:22，管理员又从正确
启动器创建一次性票据并完成 MFA；生产审计页可见会话创建、票据兑换和双重验证完成。
随后以只读方式逐页核对服务器目录、玩家与权限、客户端档案、运行数据、服务状态、
告警中心、诊断包和审计记录。六个档案均启用，24 小时、7 天和 30 天窗口均正常加载，
五个目标心跳完整，Lobby 为零玩家。告警中心只有一条恐怖整蛊磁盘余量警告且严重告警
为零；本轮未确认告警，也未执行目录、账号、档案或权限写入。脱敏证据见
[`evidence/ADMIN_WEB_VISUAL_ACCEPTANCE_2026-07-30.json`](evidence/ADMIN_WEB_VISUAL_ACCEPTANCE_2026-07-30.json)。
这次逐页目视验收不替代任何使用专门测试对象并带回滚的管理写入验收。

2026-08-02 09:46 CST，API `0.26.1-20260802T012527Z` 将九个管理模块正式切换到
Vue 3。数据库与配置备份、原子切换、本机/公网健康、九个深层路由、Host 锁定和静态
资源哈希均通过。09:52 CST 又从正式启动器创建真实一次性票据；可信设备直接完成 MFA，
地址栏最终为 `/admin/servers` 且 fragment 为空。随后逐页等待异步数据稳定，确认九页
均无资源错误、骨架屏残留、横向溢出或破图，浏览器 warning/error 为 `0`。本轮未确认
告警、未修改目录/账号/档案/权限/服控，也未操作 Minecraft 服务端。当前活动告警仍为
两条停服心跳 Critical 与两条真正 PVP Warning，保持未确认。完整记录见
[`API_RELEASE_0.26.1.md`](API_RELEASE_0.26.1.md) 与
[`evidence/API_0.26.1_PRODUCTION_DEPLOYMENT_2026-08-02.json`](evidence/API_0.26.1_PRODUCTION_DEPLOYMENT_2026-08-02.json)。

API `0.16.0` 包含玩家账号安全抽屉、论坛 Cookie 联动、受控全局等级和管理端点，完整生效范围、
迁移、审计和回滚见
[`ADMIN_ACCOUNT_SECURITY_OPERATIONS.md`](ADMIN_ACCOUNT_SECURITY_OPERATIONS.md)。

API `0.17.0` 在“客户端档案”页增加完整发布工作台：创建档案、原样导入签名清单、
查看版本元数据、设置 Test/Gray/Production、调整稳定灰度比例、回滚通道、
暂停问题版本和恢复发布。Production 指派、回滚与暂停均使用独立确认界面；
修订冲突会刷新当前档案，不会静默覆盖。详细规则见
[`ADMIN_CATALOG_OPERATIONS.md`](ADMIN_CATALOG_OPERATIONS.md)。

API `0.18.0` 在“运行数据”页增加 24 小时、7 天和 30 天聚合。页面只显示事件数、
独立用户数、安装/修复与启动成功率、传输字节、启动器版本、档案版本和固定失败分类，
不提供用户明细、文件路径或异常文本。候选已通过隔离管理员会话验收；真实管理员
已在正式页面完成 MFA，并切换三个时间窗口核对真实样本。完整边界见
[`LAUNCHER_TELEMETRY_OPERATIONS.md`](LAUNCHER_TELEMETRY_OPERATIONS.md)。

API `0.19.0` 在“服务状态”页增加心跳、在线人数、进程内存、CPU、磁盘、启动时间、
TPS/MSPT 和固定问题摘要。该页只读且没有启动、停止或重启按钮。生产数据库已经收到
采集器 `0.2.0` 的进程与磁盘指标；当前生产已升级到采集器 `0.2.1`，真实管理员
已核对五目标心跳、在线人数、进程、磁盘和 TPS/MSPT/空服暂停状态。页面仍保持只读。

API `0.20.0` 在“运行告警”页增加当前告警、级别、来源、首次/最后出现时间和固定摘要。
该页只读；告警来源包括 API 错误/延迟、登录与下载失败、游戏服运行状态、公网入口、
TLS 证书和异地备份。平台监控器只在告警变化或恢复时发送邮件。详细边界见
[`OPERATIONAL_ALERTS.md`](OPERATIONAL_ALERTS.md)。

## 7. 服控面板

第九个“服控面板”支持结构化启动、停止和重启、冲突服先停后启、五项
`server.properties` 快捷设置、受限 Minecraft 控制台和操作历史。它不提供
PowerShell、CMD、SSH、任意文件浏览或任意进程终止能力。

Vue 页面每 3 秒读取轻量的 `GET /v1/admin/server-control/overview`，该接口只返回
目标摘要和进行中的操作。当前选中服务器的控制台尾部、命令白名单和最近 20 条操作
由 `GET /v1/admin/server-control/targets/{serverId}` 单独读取，避免每次轮询携带所有
服务器日志和全局历史。部署 API 与静态页面之间存在短暂版本交错时，已打开的原生旧
页面会把缺失字段按空值处理并暂时隐藏历史；刷新页面后即进入 Vue 版本。

服控页使用 `/admin/control?server=<serverId>` 作为稳定的单服入口。服务器目录中带
服控目标的记录和整合包部署结果都会跳到精确目标；无效或已删除的目标会回退到首个
可管理目标并明确告警。服务器目录中的 `Online`、`Maintenance`、`Closed` 是玩家入口
策略，不是进程开关；实际启动、停止和首次冷启动结果只以服控面板的操作历史为准。

上述精确交接已随 API `0.30.5-20260814T121420Z` 进入生产。TypeScript、Vitest
`11/11`、Playwright `30/30`、API `315/315` 和完整解决方案 `735/735` 通过；生产静态
文件、深层路由、本机与公网健康检查均已核对。本次发布没有自动启动、停止或重启任何
Minecraft 服务端。

生产部署必须先保持 `ServerControl__Enabled=false`。两台游戏 VPS 的真实目录、
端口、计划任务、冲突组和控制台桥完成只读盘点后，只能使用专门的无玩家测试目标
完成首次启停验收。启动冲突组中的目标时，所有在线冲突服必须先成功停止；任一停止
失败都会取消目标启动并写入审计。完整部署、双重校验和回滚步骤见
[`SERVER_CONTROL_AGENT_OPERATIONS.md`](SERVER_CONTROL_AGENT_OPERATIONS.md)。

## 8. 回滚

应用故障时可把 API `current` 链接切回直接回滚目标
`0.30.4-20260814T093000Z`。迁移 5 至 28 均为加法或兼容变更，旧 API 不读取新增表与
字段，回滚时不要删除 MFA、会话、访问规则、UUID 封禁、请求指标、告警或审计记录。

若只需关闭可信设备功能，先把现有 `launcher.admin_trusted_devices` 行标记为撤销并回滚上一 API；不要删除 MFA 凭据。若需关闭整个管理后台，先将 `AdminWeb__Enabled=false`，再按 API 标准发布流程重启 `hechao-launcher-api.service`。这些操作不要求也不允许重启大厅、生存服、活动服、Velocity 或其他 Minecraft 服务。

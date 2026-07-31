# 管理员服务器目录 API

> 源码候选：API `0.21.0`（隔离验收通过、未部署；基础目录自 `0.7.0` 起保持兼容）
> 生产状态：API `0.24.1-20260731T105946Z` 已部署；真实管理员 MFA 已登记，目录写入和物理服状态同步已完成生产验收
> 安全边界：只管理目录数据，不包含 Minecraft、Velocity 或 Java 进程的启动、停止、重启和命令执行能力

## 1. 访问控制

所有端点位于 `/v1/admin`，只接受 `admin.hechao.world` 上完成 MFA 的独立浏览器会话，并要求当前用户的 LuckPerms 映射等级仍为 `Administrator`。API 每次请求都从数据库重新读取用户状态；普通成员、活动成员、协作者以及只持有启动器 Bearer 的请求都不能直接访问。

启动器 Bearer 只用于创建 90 秒一次性后台票据，不进入网页。票据兑换、浏览器 Cookie、TOTP、恢复码、CSRF 和主机锁定详见 [`ADMIN_WEB_OPERATIONS.md`](ADMIN_WEB_OPERATIONS.md)。

管理员目录限流按已认证用户划分，每分钟最多 `240` 次请求。所有写请求还必须通过 antiforgery 校验。响应继续使用 `Cache-Control: no-store`、`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY` 和请求追踪 ID。

## 2. 端点

| 方法与路径 | 用途 |
| --- | --- |
| `GET /v1/admin/catalog/servers` | 查看全部服务器，包括已归档记录和当前修订号 |
| `GET /v1/admin/catalog/servers/{serverId}` | 按 ID 查看单个服务器及当前修订号 |
| `GET /v1/admin/catalog/client-profiles` | 查看客户端档案、三个发布通道、启用状态和发布数量 |
| `GET /v1/admin/catalog/client-profiles/{profileId}` | 查看档案、通道和全部不可变发布 |
| `POST /v1/admin/catalog/client-profiles` | 创建空档案和 Test、Gray、Production 三个通道 |
| `PUT /v1/admin/catalog/client-profiles/{profileId}` | 修改显示名，或在正式通道存在可用发布后启用档案 |
| `POST /v1/admin/catalog/client-profiles/{profileId}/releases` | 导入并验证离线签名的原始 JSON 清单 |
| `PUT /v1/admin/catalog/client-profiles/{profileId}/channels/{channel}` | 为通道指定发布和测试/灰度比例 |
| `POST /v1/admin/catalog/client-profiles/{profileId}/channels/{channel}/rollback` | 按发布时间回退到该通道的上一份可用发布 |
| `PUT /v1/admin/catalog/client-profiles/{profileId}/releases/{sha256}/pause` | 暂停或恢复发布；暂停时自动移走所有通道指针 |
| `POST /v1/admin/catalog/servers` | 新增服务器，可先以隐藏状态创建 |
| `PUT /v1/admin/catalog/servers/{serverId}` | 编辑显示、状态、容量、版本、加载器、等级、档案、Velocity 目标和排序 |
| `PUT /v1/admin/catalog/servers/{serverId}/visibility` | 归档或恢复服务器，不物理删除 |
| `GET /v1/admin/users?query=<text>&limit=<n>` | 按账号、显示名、邮箱或 Minecraft 身份搜索玩家 |
| `GET /v1/admin/users/{userId}/access-preview` | 预览玩家对全部服务器的最终访问结果与原因 |
| `PUT /v1/admin/users/{userId}/access-rules/{serverId}` | 新增或更新单服允许/拒绝规则、原因和到期时间 |
| `DELETE /v1/admin/users/{userId}/access-rules/{serverId}` | 按期望修订号删除单服规则 |
| `GET /v1/admin/audit-logs?limit=100&beforeId=<id>` | 按 ID 倒序读取审计记录，最多每页 `200` 条 |

服务器 ID 创建后不可修改。归档只把 `is_visible` 设为 `false`，不会删除访问例外、心跳历史或审计记录，也不会终止对应 Java 进程。

目录状态语义：

- `Online`：允许目录根据新鲜心跳展示在线状态；它不会启动服务端。
- `Maintenance`：客户端固定显示维护中，即使目标端口在线也不能进服。
- `Closed`：客户端固定显示未开放；它不会关闭服务端进程。

从 API `0.24.1` 起，以上状态是管理员策略，不再要求管理员用 `Closed` 模拟停服：

- `Maintenance` 和 `Closed` 始终优先，分别固定显示维护中和未开放。
- `Online` 且存在同名 `server_control_targets.server_id` 时，物理服新鲜上报在线才显示开放；上报停止时显示“服务已停止”，代理心跳过期时显示“服控失联”。
- 物理服重新运行并恢复新鲜心跳后自动开放，不需要再次编辑目录。
- Velocity 心跳继续提供共享入口和在线人数；服控目标负责区分共享 `activity` 入口背后的具体物理服。
- 管理后台目录每 5 秒刷新一次实际状态。活动后端应保留 `Online` 策略，由 `owl5-activity-slot` 中当前物理服的运行状态决定玩家是否可见和可进。
- owl9 历史 ID `pvp` 是恐怖整蛊；真正 PVP 是 `pvp-purpur`，不得混用。

配置状态为 `Online` 时，`opensAt` 之前和 `closesAt` 及之后会解析为 `Closed`；
处于开放窗口时继续合并新鲜心跳。手动 `Maintenance` 或 `Closed` 不受排期覆盖。
公告会随玩家目录返回，启动器使用它替换默认服务器说明。

最终访问优先级为：禁用账号、未绑定 Minecraft、已归档服务器、非在线状态、
有效拒绝规则、有效允许规则、LuckPerms 数据新鲜度、全局最低等级。到期规则自动
退回等级判定；允许规则不能绕过账号禁用、未绑定、归档或服务器关闭。

## 3. 客户端发布通道

API `0.17.0` 把“客户端档案”和“某次签名发布”分开。档案 ID 创建后不变，
每次发布以签名清单原始字节的 SHA-256 为不可变主键，保存到：

```text
/var/lib/hechao-launcher-api/manifests/releases/<profile-id>/<sha256>.json
```

上传端不能提交版本号、下载大小或加载器等可伪造元数据。API 使用内嵌只读公钥
信任包验证 Ed25519 签名，再从签名负载提取档案 ID、版本、Minecraft、Java、
加载器、文件数、逻辑大小和发布时间。路由档案 ID 与签名负载不一致、未知 Key ID、
签名错误、摘要错误、超限或重复版本都会被拒绝。

每个档案固定拥有：

- `Test`：只对 `Administrator` 生效，比例可设为 `0` 至 `100`。
- `Gray`：对已登录账号生效，比例可设为 `0` 至 `100`。
- `Production`：正式兜底，比例固定为 `100`。

测试和灰度使用 `userId + profileId + channel` 的 SHA-256 稳定分桶。同一账号在比例
不变时不会随机跳组；匿名目录只解析正式通道。优先级为 Test、Gray、Production。
被暂停的发布永远不能被解析。暂停某个正在使用的发布时，API 在同一事务内把受影响
通道回退到按 `publishedAt` 排序的上一份未暂停发布；没有候选时清空通道并禁用档案。
恢复发布只解除暂停，不会自动重新推广。

新档案默认禁用。只有正式通道已指向未暂停发布时才能启用。正式推广、回滚、暂停、
恢复和档案启停均要求管理员 MFA、CSRF、期望修订号和审计。生产发布的标准顺序是：

1. 离线发布器生成并验证签名清单，内容对象先写入私有 OSS。
2. 后台创建档案或打开现有档案，原样导入签名 JSON。
3. 指向 Test，并由管理员账号完成安装、修复和启动验证。
4. 指向 Gray，先设小比例并观察下载与启动结果。
5. 明确二次确认后指向 Production，再启用档案。

迁移 14 存在后，`deploy/linux/publish-profile.sh` 会在修改文件或数据库前退出。
不得再用旧脚本直接改 `client_profiles`，否则会绕过签名验证、通道修订和审计。

## 4. 并发与验证

数据库迁移 5 为每个服务器增加从 `1` 开始的 `revision`。编辑、归档和恢复必须提交上次读取到的 `expectedRevision`：

- 修订号一致：变更成功，服务器修订号加一。
- 修订号过期：返回 `409 Conflict`，响应包含当前服务器记录。
- 客户端应刷新记录、让管理员重新确认，不得静默覆盖。

新增和编辑会验证：

- ID、客户端档案 ID 和 Velocity 目标格式。
- 显示名、短名称和图标字符长度及控制字符。
- Minecraft 版本、加载器、最低等级、人数上限和排序范围。
- 公告最多 `280` 个字符；开放时间必须早于关闭时间。
- 绑定的客户端档案必须存在且处于启用状态。
- 单服规则原因最多 `240` 个字符；到期时间必须晚于当前时间。
- 档案 ID 为 2 至 64 位小写字母、数字、点、下划线或短横线。
- 档案显示名为 1 至 80 个可显示字符。
- Test 和 Gray 比例为 0 至 100；Production 固定为 100。
- 档案、通道和发布暂停分别使用自己的修订号，过期写入返回 `409 Conflict`。

`velocity_target` 允许多个目录服务器共享，不应添加唯一约束。自 `2026-07-31` 起，
不同活动目录记录统一共享 `activity` 目标，并由 `owl5-activity-slot` 保证同一时刻只有
一个物理活动后端可进入。历史活动复用 `survival2` 的记录只用于迁移盘点，不得作为
新建或改版活动的模板；完整规则见
[`ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md`](ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md)。

## 5. 审计

新增、编辑、归档和恢复均在服务器变更的同一 PostgreSQL 事务中写入 `launcher.audit_logs`。任一步失败时两者一起回滚。

每条记录包含：

- 操作者内部用户 ID 与可联查显示名。
- 动作、目标类型和服务器 ID。
- Nginx 转发后由 API 接收到的来源 IP。
- 变更前和变更后的完整目录快照。
- UTC 创建时间。

当前动作名称：

```text
catalog.server.created
catalog.server.updated
catalog.server.archived
catalog.server.restored
access.server_rule.created
access.server_rule.updated
access.server_rule.deleted
catalog.client_profile.created
catalog.client_profile.updated
catalog.client_profile.enabled
catalog.client_profile.disabled
catalog.client_profile_release.imported
catalog.client_profile_release.hydrated
catalog.client_profile_release.paused
catalog.client_profile_release.resumed
catalog.client_profile_channel.updated
catalog.client_profile_channel.rolled_back
```

重复提交相同可见性属于幂等成功，不增加修订号，也不制造无变化审计记录。

## 6. 部署与回滚

本功能代码、数据库结构、公网管理入口与管理员 MFA 已部署。正式写入验收前必须：

1. 生成并校验目标 API Linux 发布物、提交号与 SHA-256；当前生产为 `0.22.0-20260729T144953Z`。
2. 创建部署前数据库备份，运行 `pg_restore --list` 验证可读。
3. 确认至少一个真实 `Administrator` 身份可用于授权测试。
4. 部署 API 后验证迁移 5、迁移 6、迁移 10、迁移 14、`healthz`、`readyz` 和旧目录端点。
5. 验证普通账号不能创建后台票据，管理员必须完成 MFA 后才能读取目录。
6. 只在维护窗口创建一条隐藏测试服务器，核对审计后再归档。
7. 导入两份真实签名清单，验证不可变存储、三通道、回滚、暂停和修订冲突。
8. 回归 `hechao.world`、`api.hechao.world`、启动器目录、分发和心跳。

当前生产故障时先使用
`/var/backups/hechao-launcher/api-pre-0.22.0-20260729T150635Z` 恢复上一程序与配置；
发布前实际 API 为 `0.21.0`、迁移 `018`。
迁移 5、迁移 6、迁移 10、迁移 11 与迁移 14 都是加法变更，旧版本会忽略新增表，
不需要在故障回滚中删除。若因专项故障继续回滚到 `0.16.0`，目录仍会读取迁移时
同步到 `client_profiles` 的正式发布快照，但不能使用新通道操作。禁止为回滚执行
`DROP COLUMN`、`DROP TABLE` 或删除审计记录。

本功能的部署只需要重启 `hechao-launcher-api.service`，不需要也不允许重启 Minecraft、Velocity、大厅、生存服或活动服。

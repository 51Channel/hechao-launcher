# 管理员账号安全操作

> 目标版本：API `0.16.0`
>
> 当前状态：`0.15.0-20260726T202540Z` 已生产部署，隔离端到端验收通过；等待真实管理员 MFA 页面验收
>
> 边界：管理平台账号、启动器会话、后台会话、进服授权和 Minecraft UUID；不控制任何游戏进程

## 1. 管理能力

完成 MFA 的 `Administrator` 可以在“玩家与权限”页打开账号安全抽屉：

- 停用或恢复赫朝登录账号。
- 查看并单独撤销仍有效的启动器设备会话。
- 一次撤销启动器会话、后台会话、后台登录票据和未消费进服授权。
- 对已绑定的 Minecraft UUID 建立长期或定时封禁，并按修订号解除封禁。
- 通过大厅 LuckPerms 代理提交四级全局等级变更。
- 查看每次操作产生的审计记录。

所有写请求都要求管理域名、独立后台 Cookie、MFA、CSRF 和管理员等级。操作原因必须为
4 至 500 个无控制字符的文本。

## 2. 生效范围

停用账号后：

- 新的论坛/赫朝账号密码登录会被拒绝。
- 现有启动器访问令牌、刷新令牌、后台会话、登录票据和未消费进服授权被事务内撤销。
- 后续启动器认证、管理员认证和 Velocity 授权都会拒绝该账号。

论坛已经签发的 Cookie 使用论坛 SQLite 中独立的 `sessionVersion`。API `0.16.0`
在撤销全部认证状态的 PostgreSQL 事务中写入 outbox，再由后台投递器调用论坛本机
幂等端点增加 `sessionVersion`。这不是跨数据库原子事务；投递失败会保留请求并指数退避
重试，管理页面显示待投递数量。论坛端按请求 ID 去重，因此重复投递不会重复增加版本。

UUID 封禁后：

- 绑定或使用该 UUID 建立新的 Minecraft 游戏会话会返回 `403`。
- 已有平台认证状态在同一事务中撤销。
- 目录与客户端下载对该账号返回不可访问。
- Velocity 最终授权返回 `MinecraftIdentityBanned`。
- UUID 仍保持与账号绑定，解除封禁不会改变账号停用状态。

## 3. API

| 方法与路径 | 用途 |
| --- | --- |
| `GET /v1/admin/users/{userId}/security` | 查看账号、设备会话、即时凭据和 UUID 封禁 |
| `POST /v1/admin/users/{userId}/account/disable` | 停用账号并撤销认证状态 |
| `POST /v1/admin/users/{userId}/account/enable` | 恢复账号 |
| `POST /v1/admin/users/{userId}/sessions/revoke-all` | 撤销全部平台认证状态 |
| `POST /v1/admin/users/{userId}/sessions/{sessionId}/revoke` | 撤销一个启动器设备会话 |
| `PUT /v1/admin/users/{userId}/access-tier` | 提交受控 LuckPerms 全局等级变更 |
| `PUT /v1/admin/users/{userId}/minecraft-ban` | 新建或更新 UUID 封禁 |
| `DELETE /v1/admin/users/{userId}/minecraft-ban` | 按修订号解除 UUID 封禁 |

服务端拒绝管理员停用或封禁自身，并使用全局事务 advisory lock 防止并发停用最后一名
可用管理员。UUID 绑定、解绑和封禁共用按 UUID 派生的事务锁，避免封禁与绑定竞态。

## 4. 数据与审计

迁移 `11` 新增 `launcher.minecraft_identity_bans`，迁移 `12` 新增论坛会话撤销
outbox，迁移 `13` 新增 LuckPerms 等级命令及租约。记录保留创建、更新、撤销、到期、
操作者、原因和修订号，不物理删除历史封禁或等级命令。

审计动作：

```text
security.account.disabled
security.account.enabled
security.sessions.revoked_all
security.session.revoked
security.minecraft_ban.created
security.minecraft_ban.updated
security.minecraft_ban.revoked
luckperms.tier_change.queued
luckperms.tier_change.completed
```

审计数据不保存访问令牌、刷新令牌、Cookie、User-Agent 哈希或其他可复用凭据。

## 5. 部署验收

1. 备份 PostgreSQL、当前 API 发布和环境文件，并验证转储可读。
2. 在隔离数据库运行迁移 `11` 至 `13`，使用临时管理员完成账号停用/恢复、单设备撤销、
   全部撤销、UUID 封禁/解除和修订冲突测试。
3. 验证最后管理员与当前管理员自保护。
4. 验证封禁 UUID 的目录、对象下载、Minecraft 绑定和 Velocity 授权均被拒绝。
5. 验证解除 UUID 封禁后账号停用状态不会被意外恢复。
6. 原子部署 API，只重启 `hechao-launcher-api.service`，不操作 Minecraft 或 Velocity。
7. 回归三个 HTTPS 入口、旧论坛、中转 API、目录、心跳和生产日志。
8. 使用真实管理员 MFA 再执行一次只针对测试账号的页面验收，并核对审计。
9. 使用测试账号完成 `default -> vip -> default`，并核对大厅代理与论坛 Cookie 撤销。

2026-07-27 已从生产备份恢复唯一临时数据库，并在独立端口完成步骤 2 至 5 的自动化
端到端验收；最终发布随后只重启 `hechao-launcher-api.service` 并通过公网与旧业务回归。
完整发布 ID、制品哈希、备份和证据见 [`API_RELEASE_0.15.0.md`](API_RELEASE_0.15.0.md)。
当前生产 MFA 凭据数仍为 `0`，因此第 8、9 步的真实页面与游戏内验收仍待执行。

## 6. 回滚

迁移 `11` 是加法变更。应用可以切回 `0.14.1-20260726T190856Z`，旧版本会忽略
封禁表，但也不会执行 UUID 封禁判定。因此回滚前必须确认没有依赖 UUID 封禁维持安全
边界的活跃事件。故障回滚不删除封禁记录和审计记录。

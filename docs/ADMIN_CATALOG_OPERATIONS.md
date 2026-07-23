# 管理员服务器目录 API

> 源码版本：API `0.8.0`
> 生产状态：尚未部署，线上仍为 API `0.6.0`
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
| `GET /v1/admin/catalog/client-profiles` | 查看可绑定的客户端档案及启用状态 |
| `POST /v1/admin/catalog/servers` | 新增服务器，可先以隐藏状态创建 |
| `PUT /v1/admin/catalog/servers/{serverId}` | 编辑显示、状态、容量、版本、加载器、等级、档案、Velocity 目标和排序 |
| `PUT /v1/admin/catalog/servers/{serverId}/visibility` | 归档或恢复服务器，不物理删除 |
| `GET /v1/admin/audit-logs?limit=100&beforeId=<id>` | 按 ID 倒序读取审计记录，最多每页 `200` 条 |

服务器 ID 创建后不可修改。归档只把 `is_visible` 设为 `false`，不会删除访问例外、心跳历史或审计记录，也不会终止对应 Java 进程。

目录状态语义：

- `Online`：允许目录根据新鲜心跳展示在线状态；它不会启动服务端。
- `Maintenance`：客户端固定显示维护中，即使目标端口在线也不能进服。
- `Closed`：客户端固定显示未开放；它不会关闭服务端进程。

## 3. 并发与验证

数据库迁移 5 为每个服务器增加从 `1` 开始的 `revision`。编辑、归档和恢复必须提交上次读取到的 `expectedRevision`：

- 修订号一致：变更成功，服务器修订号加一。
- 修订号过期：返回 `409 Conflict`，响应包含当前服务器记录。
- 客户端应刷新记录、让管理员重新确认，不得静默覆盖。

新增和编辑会验证：

- ID、客户端档案 ID 和 Velocity 目标格式。
- 显示名、短名称和图标字符长度及控制字符。
- Minecraft 版本、加载器、最低等级、人数上限和排序范围。
- 绑定的客户端档案必须存在且处于启用状态。

`velocity_target` 允许多个目录服务器共享。这是活动替换服复用 `survival2` 代理目标时需要的结构，不应添加唯一约束。

## 4. 审计

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
```

重复提交相同可见性属于幂等成功，不增加修订号，也不制造无变化审计记录。

## 5. 部署与回滚

本功能未部署。正式部署前必须：

1. 生成并校验 API `0.8.0` Linux 发布物与 SHA-256。
2. 创建部署前数据库备份，运行 `pg_restore --list` 验证可读。
3. 确认至少一个真实 `Administrator` 身份可用于授权测试。
4. 部署 API 后验证迁移 5、迁移 6、`healthz`、`readyz` 和旧目录端点。
5. 验证普通账号不能创建后台票据，管理员必须完成 MFA 后才能读取目录。
6. 只在维护窗口创建一条隐藏测试服务器，核对审计后再归档。
7. 回归 `hechao.world`、`api.hechao.world`、启动器目录、分发和心跳。

回滚应用时可以把 `current` 链接切回 API `0.6.0`。迁移 5 与迁移 6 都是加法变更，旧版本会忽略新增字段和表，不需要在故障回滚中删除。禁止为回滚执行 `DROP COLUMN`、`DROP TABLE` 或删除审计记录。

本功能的部署只需要重启 `hechao-launcher-api.service`，不需要也不允许重启 Minecraft、Velocity、大厅、生存服或活动服。

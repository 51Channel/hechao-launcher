# 启动器数据库运维

> 当前数据库：PostgreSQL 16
> 当前用途：服务器目录、客户端档案、Minecraft 身份、会话、LuckPerms 权限、一次性进服授权和审计数据

## 1. 运行边界

- Compose 目录：`/opt/hechao-launcher-database`
- 容器：`hechao-launcher-postgres`
- 数据卷：`hechao-launcher-postgres-data`
- 主机监听：仅 `127.0.0.1:5433`
- 数据库：`hechao_launcher`
- 应用角色：`hechao_api`，非超级用户，无建库和建角色权限
- API 环境文件：`/etc/hechao-launcher-api/environment`，权限 `600`
- 数据库秘密文件：`/opt/hechao-launcher-database/.env`，权限 `600`

秘密文件不得复制到仓库、日志、启动器客户端或运维文档。数据库端口不得加入 UFW 公网规则。

## 2. 健康检查

```bash
docker inspect --format '{{.State.Health.Status}}' hechao-launcher-postgres
ss -lntp '( sport = :5433 )'
curl -fsS http://127.0.0.1:8090/readyz
curl -fsS https://launcher-api.hechao.world/v1/catalog
journalctl -u hechao-launcher-api.service -p warning --since today --no-pager
```

预期数据库容器为 `healthy`，`5433` 只绑定 `127.0.0.1`，API 就绪响应包含 `database: ready`。

## 3. 迁移规则

迁移作为 API 嵌入资源发布。API 启动时取得 PostgreSQL advisory transaction lock，验证已执行迁移的 SHA-256，并在同一事务内执行新迁移。已发布迁移不得修改；后续变更必须新增编号更大的 SQL 文件，并保持先扩展、后清理的兼容顺序。

当前迁移：

| 版本 | 名称 | 内容 |
| --- | --- | --- |
| `1` | `initial_catalog_and_identity` | 客户端档案、服务器目录、用户、Minecraft 身份、单服授权、审计日志 |
| `2` | `authentication_and_luckperms` | 令牌哈希会话、LuckPerms 组映射与玩家快照、身份同步状态 |
| `3` | `velocity_authorization` | 10 分钟一次性启动授权、消费/撤销状态、代理目标与实例审计字段 |

## 4. 自动备份

- timer：`hechao-launcher-db-backup.timer`
- service：`hechao-launcher-db-backup.service`
- 目录：`/var/backups/hechao-launcher/database`
- 格式：PostgreSQL custom format
- 保留：14 天本机副本

手动触发和验证：

```bash
systemctl start hechao-launcher-db-backup.service
systemctl show hechao-launcher-db-backup.service -p Result -p ExecMainStatus
systemctl list-timers hechao-launcher-db-backup.timer --no-pager
```

每个 `.dump` 都有同名 `.sha256`。校验和与 `pg_restore --list` 均通过，才算有效备份。

API `0.5.0` 上线前生成的备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T102842Z.dump`，SHA-256 `f6455e523cebc2ca6ca98d3b0c3ab7eebe4e87489141f3ae4dcf954191e12efc`，校验和与目录读取均通过。上线后迁移记录 `1`、`2`、`3` 已核对。

## 5. 恢复边界

不得直接把备份覆盖到正在运行的生产库。恢复演练应先创建独立临时数据库，导入最近备份，核对迁移记录、表数量、目录记录和权限，再删除临时数据库。生产恢复需要先停止 API 写入、额外生成一次备份、记录当前数据卷和发布 ID，然后在维护窗口切换。

2026-07-22 已把首份备份恢复到唯一命名的临时验证库，核对迁移版本、3 个客户端档案、4 个服务器和 0 个初始用户后删除临时库，生产 API 全程保持就绪。仍未完成异地主机恢复和异地复制，因此不能把本机数据盘视为唯一可靠副本。

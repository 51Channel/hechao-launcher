# 启动器数据库运维

> 当前数据库：PostgreSQL 16
> 当前用途：服务器目录、客户端档案、Minecraft 身份、会话、LuckPerms 权限、一次性进服授权、Velocity 目标心跳、运行指标、诊断上传、单服访问规则、客户端遥测和审计数据

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
| `4` | `server_heartbeats` | 按 Velocity 目标保存在线状态、人数、版本和采集/接收时间 |
| `5` | `admin_catalog_revision` | 服务器目录修订号与审计目标索引 |
| `6` | `admin_web_sessions` | 后台票据、独立浏览器会话、TOTP 凭据与注册状态 |
| `7` | `hechao_accounts` | 赫朝账号名、邮箱与密码哈希 |
| `8` | `forum_account_bridge` | 显示名称唯一约束与论坛外部身份映射 |
| `9` | `diagnostic_uploads` | 玩家诊断上传、一次性令牌、配额、状态与到期清理 |
| `10` | `admin_access_and_server_schedules` | 服务器公告/开放排期、单服规则修订号与查询索引 |
| `11` | `admin_account_security` | 平台账号状态、设备与平台会话撤销、Minecraft UUID 定时封禁 |
| `12` | `forum_session_revocation_outbox` | 论坛 Cookie 撤销 outbox、租约、重试和幂等投递状态 |
| `13` | `luckperms_tier_change_commands` | 固定四级全局等级命令、代理认领、完成状态与历史索引 |
| `14` | `client_profile_release_channels` | 不可变签名发布、Test/Gray/Production 通道、暂停和修订号 |
| `15` | `launcher_telemetry` | 30 天客户端运行事件、幂等主键和聚合索引 |
| `16` | `server_runtime_metrics` | 进程、磁盘、TPS/MSPT/GC 当前值、30 天幂等分钟样本与问题分类 |

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

API `0.5.0` 上线前生成的备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T102842Z.dump`，SHA-256 `f6455e523cebc2ca6ca98d3b0c3ab7eebe4e87489141f3ae4dcf954191e12efc`。API `0.6.0` 部署并写入首批心跳后的备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T124326Z.dump`，SHA-256 `508b37c7a695413e2a3d3d5b7ff08212f720077121bb7237c522957ec08d9464`。API `0.9.0` 发布前备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T195226Z.dump`，大小 `48,720` 字节，SHA-256 `621638f3500680e7ad3903cab62ac40a974defe0ecb65a4eb9cfc292cd5547d6`。

API `0.10.0` 发布前备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T101600Z.dump` 为 `63,799` 字节，SHA-256 `9ceaaea545525e1a6ec199d11aa62fecad4e62220641cc847da2a7d1bb3f64f8`。API `0.10.1` 热修复前备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T102852Z.dump` 为 `63,846` 字节，SHA-256 `d15397bfb1c318f4141ce97a13ac2a4692c755915ff46bdd9c46c5c6b051d1d4`。五份备份的校验和与目录读取均通过，迁移记录 `1` 至 `7` 已由 API 启动校验。

启动器 `0.11.6` 小范围测试前基线备份
`/var/backups/hechao-launcher/database/hechao-launcher-20260726T084308Z.dump`
为 `85,620` 字节，SHA-256
`199e8811da08e9f9c2f1db88866f9dd51574ab9b043b6ba147b3092ad0413c36`。
备份服务结果为 `success`，同名校验文件和 `pg_restore --list` 均通过；生成过程
没有重启 API。

API `0.14.1` 发布前、迁移 10 已应用后的数据库备份
`/var/backups/hechao-launcher/database/hechao-launcher-20260726T191147Z.dump`
为 `94,908` 字节，SHA-256
`c2d9563544bffdf4060bc51ff93a5c27d1d13c84c1d25f6ec3c963aaa7181029`。
同名校验文件与 `pg_restore --list` 均通过；当前迁移记录为 `1` 至 `10`。

API `0.15.0` 发布前数据库备份
`/var/backups/hechao-launcher/database/hechao-launcher-20260726T202823Z.dump`
为 `95,200` 字节，SHA-256
`54a9f6c6321bc7adf10ac516e8a634c3c79724382f3d790c72d005fce142721e`。
同名校验文件与 `pg_restore --list` 均通过。该备份还原到隔离临时数据库后完成
迁移 11 与账号安全端到端验收，临时数据库随后清理。

API `0.16.0` 发布前协调备份位于
`/var/backups/hechao-unified-account/20260726T222616Z`。其中数据库 dump 为
`108,668` 字节，SHA-256
`00B4AEB14F49B596A41311FCAB89B49DB317280B55A2C7AFA1E691658D325784`；
清单中的 PostgreSQL、论坛 SQLite、论坛源码、API 发布与配置共 8 项均通过校验。
该备份完成隔离恢复与迁移 `11` 至 `13` 验收，生产部署后再次核对迁移记录为
`1` 至 `13`。

API `0.18.0` 发布前协调备份位于
`/var/backups/hechao-unified-account/20260727T000015Z`。其中数据库 dump 为
`119,611` 字节，SHA-256
`2D85CB21711B8817202A5177FF3BC96E27B7AB2B4540B5ADD9B1FE0530815C75`；
`pg_restore --list` 成功读取 145 个目录项。整份备份清单 SHA-256 为
`068D90C8E21DC4F277E78FA09951C3587F9B7D9C57CBD731E8D23D97A7BC33E6`，
API、数据库、论坛 SQLite、源码和环境文件共 8 项均通过校验。生产部署后迁移记录为
`1` 至 `15`，目录和发布记录数量保持不变。

## 5. 恢复边界

不得直接把备份覆盖到正在运行的生产库。恢复演练应先创建独立临时数据库，导入最近备份，核对迁移记录、表数量、目录记录和权限，再删除临时数据库。生产恢复需要先停止 API 写入、额外生成一次备份、记录当前数据卷和发布 ID，然后在维护窗口切换。

2026-07-22 已把首份备份恢复到唯一命名的临时验证库，核对迁移版本、3 个客户端档案、4 个服务器和 0 个初始用户后删除临时库，生产 API 全程保持就绪。仍未完成异地主机恢复和异地复制，因此不能把本机数据盘视为唯一可靠副本。

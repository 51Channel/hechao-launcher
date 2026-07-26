# 赫朝统一账号运维

> 目标版本：启动器 `0.11.0`、API `0.11.0`
> 身份源：启动器 API PostgreSQL
> 社区资料源：`hechao.world` Prisma / SQLite

## 1. 账号边界

赫朝启动器与 `hechao.world` 共用账号名、邮箱、显示名称和密码。新账号只能通过社区注册流程创建，必须先验证邮箱；启动器注册页调用同一社区接口，不再直接创建独立 API 账号。

论坛继续单独保存头像、简介、论坛角色、封禁状态、帖子、回复和通知设置。游戏等级与进服资格继续以 Microsoft / Minecraft 正版身份和 LuckPerms 为准，论坛角色不能提升游戏权限。

账号密码只保存在启动器 PostgreSQL。论坛 `User.passwordHash` 在迁移完成后写为 `unified`，不再参与玩家登录。论坛通过本机回环地址调用 API 内部桥接端点，公网请求即使持有桥接令牌也会返回 `404`。

## 2. 兼容迁移

旧论坛密码格式为：

```text
scrypt$<32 位十六进制盐文本>$<128 位十六进制摘要>
```

Node.js 计算时把盐字段的 32 个字符作为 UTF-8 文本传入 scrypt，而不是先解码为 16 字节。API 首次验证旧密码成功后，立即把该账号升级为 ASP.NET Core Identity PBKDF2；错误密码不会改写哈希。

导入脚本为 `scripts/import-unified-accounts.mjs`。默认只预览；`--apply` 才写入，`--apply --retire-local-passwords` 只在所有论坛用户均已关联后退役本地密码。导入按论坛用户 ID 幂等，网络中断后可以安全重跑。

## 3. 内部桥接

论坛只调用以下本机端点：

```text
POST /v1/internal/forum/accounts/register
POST /v1/internal/forum/accounts/authenticate
POST /v1/internal/forum/accounts/import
POST /v1/internal/forum/accounts/password/change
POST /v1/internal/forum/accounts/password/reset
POST /v1/internal/forum/accounts/profile
```

生产配置：

```text
ForumAccountBridge__InternalTokenSha256=<令牌 SHA-256>
ForumAccountBridge__AllowLegacyImport=true|false
HECHAO_IDENTITY_API_URL=http://127.0.0.1:8090/
HECHAO_FORUM_BRIDGE_TOKEN=<原始高熵令牌>
```

原始令牌只进入网站 `.env`，API 环境文件只保存 SHA-256。不得把任一值写入 Git、日志、发布包或命令历史。旧账号导入结束后必须把 `AllowLegacyImport` 改回 `false`。

## 4. 会话安全

论坛会话包含本地 `sessionVersion`。修改密码后：

- API 原子撤销该账号的启动器会话、管理员会话、后台票据和未使用的 Velocity 授权。
- 论坛递增 `sessionVersion`，使其他设备的 30 天 Cookie 立即失效。
- 当前改密页面收到新版本 Cookie，可以继续使用。

通过邮件找回密码时只递增版本，不签发新会话，因此所有旧论坛 Cookie 均失效。部署该字段时，旧版论坛 Cookie 因缺少版本字段会统一失效，玩家需要重新登录一次。

## 5. 上线顺序

1. 备份 PostgreSQL、论坛 SQLite、网站源码与 `.env`、当前 API 发布和环境文件。
2. 部署 API `0.11.0`，确认数据库迁移 `8` 和健康检查。
3. 在旧网站仍运行时应用 Prisma 扩展迁移。
4. 开启一次性旧账号导入，预览后执行并核对总数。
5. 构建并部署新网站，验证账号名/邮箱登录、注册、昵称同步、改密和找回密码。
6. 退役论坛本地密码，关闭旧账号导入入口。
7. 重新检查 `hechao.world`、`api.hechao.world`、`launcher-api.hechao.world`，并确认未触碰任何 Minecraft 或 Velocity 服务。

## 6. 回滚

API 迁移 `8` 只新增显示名称唯一索引和外部身份表，不删除原字段。API 可以回滚二进制，但旧版公开注册会重新产生不同步账号，因此回滚期间必须关闭注册。

网站迁移只新增 `launcherAccountId`、`launcherUsername` 和 `sessionVersion`。若已退役本地密码，不能只回滚网站代码；必须同时恢复部署前 SQLite 备份，或继续使用统一身份版本。恢复旧 SQLite 会丢失备份之后的论坛写入，执行前必须另做当前快照。

## 7. 生产状态

`2026-07-25` 已完成首次生产迁移：

- API 发布 ID：`0.11.0-20260725T074100Z`。
- 部署前备份：`/var/backups/hechao-unified-account/20260725T073432Z`。
- 论坛账号、统一身份和外部关联均为 `22`。
- 论坛本地密码摘要剩余 `0`，旧账号导入已关闭。
- 生产合成账号测试全部通过且残留为 `0`。
- 三个 HTTPS 域名、公开注册关闭和内部桥接公网隔离均验证通过。

`2026-07-27` 随 API `0.16.0-20260726T222124Z` 完成论坛既有 Cookie 联动撤销：

- API 在账号安全事务中写入 PostgreSQL outbox，由受租约和重试保护的 worker 投递；
- 论坛只接受回环地址、内部令牌和精确路径，公开请求继续返回 `404`；
- 生产临时账号验证了直接请求幂等、worker 投递、回执记录和 `sessionVersion`
  两阶段递增，测试数据已全部清理；
- 完整备份、哈希与回滚边界见
  [`API_RELEASE_0.16.0.md`](API_RELEASE_0.16.0.md)。

完整哈希、候选安装包和验证记录见
[`UNIFIED_ACCOUNT_RELEASE_0.11.0.md`](UNIFIED_ACCOUNT_RELEASE_0.11.0.md)。

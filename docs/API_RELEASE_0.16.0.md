# API 0.16.0 发布记录

> 生产发布 ID：`0.16.0-20260726T222124Z`
>
> 状态：生产在线
>
> 直接回滚目标：`0.15.0-20260726T202540Z`

## 1. 变更

- 管理后台增加四级全局等级修改入口。
- API 使用租约、领取序号和预期主组排队 LuckPerms 变更，不直接写 MariaDB。
- 新增大厅 Paper 代理 `HechaoLuckPermsTierAgent 0.1.0`，只允许
  `default`、`vip`、`admin`、`owner` 四个固定全局组。
- 全部认证状态撤销和 UUID 封禁会事务内写入论坛会话撤销 outbox。
- 后台 worker 调用论坛本机幂等端点递增 `sessionVersion`，使既有 Cookie 失效。
- 迁移 `12 / forum_session_revocation_outbox` 与
  `13 / luckperms_tier_change_commands` 均为加法迁移。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.16.0-20260726T222124Z.tar.gz` | `45,433,983` 字节 | `2B1568D5A72DAE09E0CED270633099AA961F11EBC730490451E1448BDA9EE4D4` |
| `Hechao.Api` | `104,046,643` 字节 | `8B932BC0BFE5C0D3D2A97460555695AD33544CC2814A96B8AF16E672F8B5CDB5` |
| `HechaoLuckPermsTierAgent-0.1.0.jar` | `324,817` 字节 | `35A9BBB17620DC2FD7245E0EA8CCAA293DC98C264DA3463AB706846ED7E42A7B` |
| 论坛撤销覆盖包 | `3,771` 字节 | `A6C645F845C3AFF3ABAE2DFF360DF12F8040E9C22F9FB8D5D0DC81105614B599` |

发布归档包含单文件程序、`Hechao.Api.staticwebassets.endpoints.json` 和完整
`wwwroot`。隔离验收显式请求管理后台 HTML、JavaScript 和 SPA fallback，避免只检查
API 就绪而漏掉管理页面。

## 3. 自动验收

- .NET 解决方案：`283/283`
- Velocity 授权插件：`11/11`
- LuckPerms 等级代理：`4/4`
- API 隔离生产备份还原：

```text
PASS: API 0.16.0 isolated account-security smoke test
Evidence: migrations=11-13, MFA=enrolled, session-revocation=verified, UUID-ban=verified
Evidence: Velocity-ban=verified, revision-conflict=verified, disable-enable=verified
Evidence: forum-revocation=queued, LuckPerms-tier-command=verified, stale-claim=blocked
```

- 论坛覆盖包在隔离源码、SQLite 与 Next.js 构建中通过：

```text
PASS: isolated forum session-revocation overlay
Evidence: build=passed, unauthorized=404, authorized-validation=400
```

- 生产临时账号验证同一论坛请求只增加一次 `sessionVersion`，API worker 投递返回
  `204` 并完成 outbox；测试用户、回执和 outbox 已精确清理。

## 4. 部署与备份

部署前一致性备份：

```text
/var/backups/hechao-unified-account/20260726T222616Z
```

| 备份 | 大小 | SHA-256 |
| --- | ---: | --- |
| `launcher-database.dump` | `108,668` 字节 | `00B4AEB14F49B596A41311FCAB89B49DB317280B55A2C7AFA1E691658D325784` |
| `api-current-release.tar.gz` | `45,546,432` 字节 | `0A8506E5B12156821D50850916025DA83922C34B606B324521D3A724ABB50752` |
| `forum.sqlite` | `221,184` 字节 | `135F4595656D7E0E7C74BFD4A3EE34F9ED46BF333CD2407A5676C55378F21E7D` |
| `forum-source.tar.gz` | `51,452,700` 字节 | `A7BA8C77E006A87191515620716F3749FB53E961EB5ED007EDB95149336EBCA8` |

`manifest.sha256` 的八项检查全部通过，数据库转储可由 `pg_restore --list` 读取。
安装脚本原子切换 `current` 后只重启 API；就绪检查通过，未触发回滚。

论坛撤销路由已经独立部署并保持 `hechao.service` 在线。大厅代理已放入
`E:\LobbyServer\plugins`，配置 ACL 已收紧，备份目录为
`E:\manual-backups\luckperms-tier-agent-20260726T223127Z`。安装前后 Java PID
集合一致，`ServerRestartPerformed=false`；插件等待服主下一次自行重启大厅后加载。

## 5. 生产回归

- 当前链接：`/opt/hechao-launcher-api/releases/0.16.0-20260726T222124Z`
- `/healthz`、`/readyz` 和公网就绪检查均返回 `200`，版本 `0.16.0`
- 管理后台 `index.html` 与 `admin.js` 返回 `200`
- `admin.hechao.world` 保持登录重定向 `302`
- `hechao.world` 与 `api.hechao.world` 均保持 `200`
- 部署后的 API journal 无 warning/error，systemd `NRestarts=0`
- 目录强制登录仍为 `false`，Velocity 仍为 `monitor`

## 6. 剩余验收与回滚

生产管理员 MFA 凭据数仍为 `0`。大厅代理加载后，需要用专用测试玩家完成
`default -> vip -> default`，核对 LuckPerms 主组、API 等级和审计，再进行四级真实账号
灰度。

如 API 故障，原子切回 `0.15.0-20260726T202540Z`。旧 API 会忽略迁移 `12`、`13`，
但不会投递论坛撤销，也不会处理新等级命令；回滚前必须核对没有未完成命令。论坛路由和
代理 JAR 可保留但不会收到旧 API 请求，不能删除历史 outbox、命令或审计记录。

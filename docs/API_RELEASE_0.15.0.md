# API 0.15.0 发布记录

> 生产发布 ID：`0.15.0-20260726T202540Z`
>
> 部署时间：`2026-07-27`（Asia/Shanghai）
>
> 直接回滚目标：`0.14.1-20260726T190856Z`

## 1. 源码与范围

- 账号安全功能提交：`ccdc98d`
- 管理浏览器授权修复与隔离验收脚本提交：`c53dbd8`
- Git 标签：`api-v0.15.0`
- 数据库迁移：`11 / admin_account_security`

本版本增加账号停用与恢复、单设备会话撤销、全部平台认证状态撤销，以及长期或定时
Minecraft UUID 封禁。账号与 UUID 状态会在登录、目录、对象下载、Minecraft 身份绑定、
管理员会话和 Velocity 最终授权中统一执行。管理界面包含修订号冲突保护、自身保护、
最后一名可用管理员保护和事务审计。

## 2. 最终制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Hechao.Api` | `103,950,899` 字节 | `42ACC44468989A567E936993934046266A9D2B22B43758322E693BC23A089FD6` |
| `hechao-api-0.15.0-20260726T202540Z.tar.gz` | `45,404,470` 字节 | `9B096CBB55636494D64148908DA7168D5B748E12086BE6C854417891FEBBF10A` |

首次候选隔离验收发现管理员 Web Cookie 缺少玩家身份声明，导致只读页面正常而账号安全
写请求返回 `403`。该问题在最终制品前修复，并新增主体声明回归测试；早期候选没有部署。

## 3. 验证

- .NET 解决方案测试：`261/261`
- Velocity Java 测试：`11/11`（插件代码未变）
- `git diff --check` 与隔离验收脚本语法检查通过
- 从生产备份恢复唯一临时数据库，在独立端口完成 API `0.15.0` 真实配置启动
- 隔离端到端验收覆盖临时账号、管理员引导、TOTP MFA、单设备/全部会话撤销、
  Velocity 授权撤销、UUID 封禁/更新/冲突/解除、账号停用/恢复、自身保护和审计
- 临时 systemd 单元、端口、数据库与目录在验收后全部清理

隔离验收最终输出：

```text
PASS: API 0.15.0 isolated account-security smoke test
Evidence: migration=11, MFA=enrolled, session-revocation=verified, UUID-ban=verified
Evidence: Velocity-ban=verified, revision-conflict=verified, disable-enable=verified
```

## 4. 部署前备份

数据库：

- `/var/backups/hechao-launcher/database/hechao-launcher-20260726T202823Z.dump`
- `95,200` 字节
- SHA-256 `54a9f6c6321bc7adf10ac516e8a634c3c79724382f3d790c72d005fce142721e`
- `pg_restore --list` 通过

API、环境、systemd 与 Nginx：

- `/var/backups/hechao-launcher/api-predeploy/pre-api-0.15.0-20260726T200900Z-full.tar.gz`
- `45,500,274` 字节
- SHA-256 `58ecffe9977c75b3e1e8c068d82048e4585b98bd53f3dd82d6895cba215c6fa7`

两份正式备份均有同名 `.sha256` 文件。早期
`pre-api-0.15.0-20260726T200820Z.tar.gz` 只包含符号链接，不作为恢复依据。

## 5. 生产结果

- `current` 指向 `/opt/hechao-launcher-api/releases/0.15.0-20260726T202540Z`
- `/healthz` 与 `/readyz` 返回 `200`，版本为 `0.15.0`，数据库就绪
- 迁移记录包含 `11 / admin_account_security`
- `hechao.world`、`api.hechao.world`、启动器 API、管理域名和匿名目录均完成回归
- 无效 Bearer 目录请求返回 `401`
- `launcher-api.hechao.world/admin/` 保持 `404`
- 管理域名保留 `no-store`、`nosniff`、`DENY`、`no-referrer` 与 CSP
- API 部署后 `NRestarts=0`，部署时间起无 warning/error 日志
- 论坛、Nginx、Minecraft 与 Velocity 服务没有被停止、启动或重启

当前生产仍保持：

- `Authentication__EnforceCatalogAuthentication=false`
- Velocity `0.2.0` 为 `monitor`
- 生产 TOTP/MFA 凭据数为 `0`

因此本次发布证明账号安全代码、迁移与生产运行基线已完成，不代替真实管理员首次 MFA、
页面操作与审计验收。论坛已签发 Cookie 的 `sessionVersion` 联动撤销也仍是独立待办。

## 6. 回滚

应用故障时将 `current` 原子切回
`/opt/hechao-launcher-api/releases/0.14.1-20260726T190856Z`，只重启
`hechao-launcher-api.service` 并重新执行健康、认证、管理域名、旧官网和中转 API 回归。
迁移 11 是加法变更，回滚时保留 UUID 封禁表和审计记录。旧版本不会执行 UUID 封禁判断，
因此存在依赖封禁维持安全边界的活动事件时不得直接回滚。

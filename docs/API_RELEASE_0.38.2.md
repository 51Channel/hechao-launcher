# 赫朝启动器 API 0.38.2 正式发布

- 发布日期：`2026-09-04`
- 发布目录：`/opt/hechao-launcher-api/releases/0.38.2-20260904T013112Z`
- 制品源码提交：`f26c4a4396953abb502972ba7657e467e6743d5a`
- 正式标签：`api-v0.38.2`
- 数据库迁移：保持 `35/35`，本版不新增迁移

## 问题与修复

商业街记录已存在于生产目录和服控目标中，但诊断时处于玩家隐藏状态。旧后台默认只
显示 `isVisible=true` 的服务器，又把 `isVisible=false` 错误称为“已归档”；新增抽屉
则正确排除所有已建档 ID，因此管理员看到目录里没有商业街，同时也没有可添加目标。

本版将后台服务器目录默认范围改为“全部”，并把筛选、汇总、状态和操作统一为“玩家
可见 / 玩家隐藏”。玩家隐藏记录仍能在后台管理，也不会进入新增服务器候选。显示状态
切换继续只修改玩家目录策略，不会启动或停止 Java 进程。

诊断后、部署前，已认证管理员在 `09:18:57` 将商业街改为玩家可见，并在 `09:19:16`
将入口策略改为 `Online`。审计记录为连续修订 `r1 -> r2 -> r3`，没有自动任务或 API
重启写入。部署保留了这项人工变更；客户端仍仅在 Test 通道，Gray/Production 未分配。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.38.2-20260904T013112Z-linux-x64.tar.gz` | `46,999,847` 字节 | `F67E349716CFDB3241D196BFD9D338E5C4510CE17205D38A68537633B2C96A71` |
| `Hechao.Api` | `105,703,492` 字节 | `542F0AADB41C98518A85FAAA517EBFF292A8262A11C37BE1A87C2C54C1C0BC4F` |

本地归档、生产可执行文件和不可变发布目录的摘要一致。制品不含 PDB、环境文件或秘密。
部署前备份为：

- API 配置、systemd、Nginx、当前发布指针、Data Protection 和清单：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.38.2-20260904T013112Z`；
- PostgreSQL：
  `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.38.2-20260904T013112Z.dump`，
  `7,964,461` 字节，SHA-256
  `B96A794030E772F0D6FBF632457D3E1B853F40245A437E04A622A74720788040`。

数据库备份已通过 `pg_restore --list`，API 快照的 `SHA256SUMS` 已全部复验。

## 生产验收

- AdminWeb Vitest `19/19`、Playwright `36/36`、完整 .NET 解决方案 `838` 项通过，
  `1` 项外部 PostgreSQL 条件测试按环境跳过；
- PowerShell 7 合规 `49/49`、发布溯源 `29/29` 和 `git diff --check` 通过；
- `current` 指向上述不可变发布目录；服务为 `active/running`，主 PID `1538900`，
  `NRestarts=0`；
- 回环和公网 `/healthz`、`/readyz` 均返回 `200`，版本为 `0.38.2`，部署后 warning
  及以上日志为 `0`；
- 数据库保持 `35/35`，发布/部署流水线、服控操作队列和服控命令队列均为 `0`；一条
  旧整合包停在 `AwaitingReview`，没有执行发布或部署；
- `admin.hechao.world/admin/servers` 返回 `200`，匿名管理员会话返回 `401`，错误 Host
  的管理入口返回 `404`，公网 `8090` 不可连接；
- 真实 Chrome 管理员会话确认默认筛选为“全部”、商业街恰好出现一行；新增抽屉显示
  `0 个可添加` 且不含商业街，浏览器 warning/error 为 `0`；
- `hechao.world` 与 `api.hechao.world` 均返回 `200`。

本次只原子切换并重启 `hechao-launcher-api.service`，没有修改 Nginx、Publisher、
Velocity，也没有启动、停止、重启或发送命令给任何 Minecraft 服务端。

## 回滚

程序故障时原子切回
`/opt/hechao-launcher-api/releases/0.38.1-20260904T002600Z`，只重启
`hechao-launcher-api.service`。本版没有数据库迁移，数据库结构无需回退；回滚前仍应
保留当前数据库快照和审计记录。

结构化证据见
[`evidence/API_0.38.2_PRODUCTION_DEPLOYMENT_2026-09-04.json`](evidence/API_0.38.2_PRODUCTION_DEPLOYMENT_2026-09-04.json)。

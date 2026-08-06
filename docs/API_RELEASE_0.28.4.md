# API 0.28.4 正式发布

- 正式发布 ID：`0.28.4-20260806T002900Z`
- 制品源码提交：`6df2765dde8e66503ba24f97529e8fd36285569a`
- 正式标签：`api-v0.28.4`
- 生产切换时间：2026-08-06 08:35:34 CST

## 修复范围

- 已删除活动服目录导致代理无法读取 `server.properties` 和 JVM 参数时，不再把
  `settings=null` 解释为部署上限 `0 MiB`；
- API 只为严格匹配 `activity / owl5 / owl5-activity-slot / 25568`、允许部署且目录
  不存在的固定槽派生 `4096 MiB` 上限；目录仍存在但设置读取失败、目标身份不匹配或
  部署能力关闭时继续拒绝；
- 管理后台、确认接口和部署编排统一使用同一上限。页面允许提交后，后台不会再因为同一
  空值返回 `DEPLOYMENT_MEMORY_INVALID`；
- 目录不存在时初始内存使用受控默认值，owl5 代理仍以本地 `8192 MiB` 配置做最终验证。

根因是目录删除后，服控代理按设计继续上报目标身份、停服状态和部署能力，但无法从已经
不存在的服务端配置文件构造快速设置。旧页面将这个空值退化为 `0 MiB`，所以其他条件
全部满足时“发布并部署”仍保持禁用。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.28.4-20260806T002900Z.tar.gz` | 45,964,802 字节 | `076B7BFF056129709F45B78D06F0704FF102506862CDB1380F256B3736907181` |
| `Hechao.Api` | 105,043,908 字节 | `BECAAF0660EC5E56C2DD26A2A0D52AE5417B635F4369A2BF70C577CD3917DD8D` |
| `chunk-PackageImportsView.js` | 27,603 字节 | `B2593CAFE89226CA5BFEA1AB2E782115C1802D5B68A6542BAB8C3E85C76E25C6` |

归档共 158 项、153 个文件；不含 PDB、环境文件、生产配置或凭据。上传归档、部署后
程序和公网后台分块哈希均与本机构建一致，后台分块返回 `Cache-Control: no-store`。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260806T003458Z.dump`；
- 数据库大小 3,457,225 字节，SHA-256：
  `101B07690B8BC58C16298233EF2E0BCEE7B97AAA7323734C74945B9AB99B4927`；
- `pg_restore --list` 可读取 225 行目录记录；
- 当前 API、环境、systemd 与 Nginx 配置备份：
  `/var/backups/hechao-launcher/releases/0.28.4-20260806T002900Z`；
- 安装器原子切换 `current`，readiness 或后置门禁失败会自动恢复 API `0.28.3`；
- 只重启 `hechao-launcher-api.service`，没有向 Minecraft、Velocity、Publisher 或
  ServerControlAgent 发送控制命令。

## 验证

- API `285/285`、完整解决方案 `.NET 673/673`；
- TypeScript、Release 构建、Vitest `8/8` 和 Playwright `16/16` 通过；
- Playwright 以 `serverFilesPresent=false`、`settings=null` 和派生上限
  `4096 MiB` 验证精确确认后按钮可点击并成功提交；
- 迁移保持 `24/24`，本版本没有新增数据库迁移；
- 生产 `activity` 心跳新鲜、已停服、目录不存在、清理完成、允许部署且设置为空，活动
  操作为 0；导入任务 `777a31bf-acc9-4754-9f4b-a3a2e5be95f1` 保持
  `AwaitingReview r7`，发布过程没有替用户提交；
- 本机和公网 `/healthz`、`/readyz` 均返回 `0.28.4`，数据库为 `ready`；
- 服务为 `active/running`、PID `887324`、`NRestarts=0`，部署后 warning/error 为 0；
- API 只监听 `127.0.0.1:8090`，公网直连不可达；官网、旧中转 API、目录和后台均为
  200，错误 Host 的后台路径为 404，无效 Bearer 为 401；
- Publisher `1.1.0` 心跳新鲜，PID `4028581` 在发布前后保持不变；未操作游戏服或代理；
- 根盘使用率为 92%，清理本次 `/tmp` 上传文件后剩余约 4.39 GB；未清理历史
  release、备份或日志。

## 回滚

本版本的直接安全回滚目标为：

`/opt/hechao-launcher-api/releases/0.28.3-20260805T234331Z`

生产已经存在 `DeleteServerFiles` 操作记录，不得回滚到 `0.27.3`。回滚只切换
`current` 并重启 API；数据库迁移 024、删除审计记录和服控目标必须保留。

结构化证据见
[`evidence/PACKAGE_IMPORT_DELETED_ACTIVITY_MEMORY_LIMIT_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/PACKAGE_IMPORT_DELETED_ACTIVITY_MEMORY_LIMIT_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

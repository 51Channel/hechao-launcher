# API 0.30.0 正式发布

- 正式发布 ID：`0.30.0-20260809T232800Z`
- 制品源码提交：`360c0fc83c77d465c6b521b6713ee71f644b7432`
- 正式标签：`api-v0.30.0`
- 生产切换时间：2026-08-10 08:03（Asia/Shanghai）
- 数据库迁移：`028_activity_plans`

## 功能范围

- Launcher PostgreSQL 成为活动企划、整合包绑定、排期状态和部署历史的唯一数据源；
- Launcher Vue 后台与官网后台提供同一组可视化月历，支持点击、框选、拖动和双端调整；
- 已发布企划使用 PostgreSQL GiST 排斥约束保证全局 `[开始, 结束)` 区间不重叠，API
  同时返回可读的 `409 schedule_conflict`；
- 只有已完成、档案未归档且精确清单进入 `Production` 的整合包可以发布；玩家可以在
  开放前下载客户端，但进服还要求排期、服务端在线、代理新鲜和活动槽部署身份完全一致；
- 官网只通过回环内部桥接读写企划，浏览器与公网路由不会取得桥接令牌、审计身份或整合包
  内部字段。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.0-20260809T232800Z.tar.gz` | 46,243,747 字节 | `E84CDA346F46A0DA3127140858FCE210C5DA73B75EC298655CD000FFF8A8EA70` |
| `Hechao.Api` | 105,230,788 字节 | `BFD43092A149035CC6E80D9F86241B59DF784C5CD2A8E5853D9BE38BF3E71F77` |
| `028_activity_plans.sql` | 迁移源码 | `DF11C4EC70AEC0D1C962753AEF9C0EE1A8C8DA59E1B82D7A9C9BFF9596005F96` |

归档共 160 项、156 个文件，只包含单文件 API、静态管理后台和端点清单；安全路径、禁止
文件和服务器哈希均通过。环境文件、数据库、PDB、令牌和凭据未进入制品。

## 测试与演练

- 当前分支完整解决方案 `.NET 719/719`，其中 API `302/302`、ServerControlAgent
  `58/58`、Launcher `225/225`；Release 构建通过；
- Vue 管理后台 Vitest `11/11`、Playwright `25/25`，覆盖创建、移动、双端 resize、
  重叠草稿、发布冲突、首尾相接、移动端布局和 WCAG A/AA；
- 正式二进制在独立临时数据库自动应用 `28/28`：A 发布成功，与 A 重叠的 B 返回
  `409 schedule_conflict`，B 改到 A 结束时刻接档后发布成功；临时数据库随后删除；
- 官网生产合同 `16/16`、Next.js 构建 `81/81` 和活动桥接测试通过。

## 备份与部署

- 迁移前 PostgreSQL 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260809T233948Z.dump`，
  4,755,868 字节，SHA-256
  `9380B924245A9485B50971950D1C6CEEC3C26CA418E69A8DB9A64697846BE5B5`；
  `pg_restore --list` 读取 227 行；
- API、环境、systemd 与旧链接备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.0-20260809T233948Z.tar.gz`，
  45,860,803 字节，SHA-256
  `903FF6C732551B4827F1A8A13732B9D11786D61D1FA83D3F98BB0BB535FECB40`，共 7 项；
- 正式 release 为 `/opt/hechao-launcher-api/releases/0.30.0-20260809T232800Z`；
- 发布顺序为 owl5 Agent、API 与迁移、内部桥接、官网候选和官网正式切换。API、网站与
  Publisher 均为 `active/running`、`NRestarts=0`，发布后 warning/error 为 0；
- 生产迁移为 `28/28`，正式企划、进行中服控命令和进行中整合包任务均为 `0`；临时
  数据库、候选单元、`18094/3100` 监听和 incoming 目录均已清理；
- 本轮没有启动、停止、重启或切换 Minecraft 与 Velocity。

## 生产联动验收

- 官网桥接无令牌与错误令牌均返回 `401`，仅正确的服务器侧令牌返回 `200`；凭据未写入
  日志、文档或命令输出；
- 官网公开主线 13 项通过，ICS 返回 `200 text/calendar`，启动器下载返回到 HTTPS 下载域
  的 `302`；公开活动投影为 `200` 且不含整合包、审计、令牌或签名 URL 字段；
- 网站 SQLite 为 59 条迁移、28/28 统一账号映射、外键错误 0；34 个历史上传和现有头像
  探针均返回 `200`。

## 回滚

当前条件程序回滚目标为
`/opt/hechao-launcher-api/releases/0.29.0-20260808T043921Z`。迁移 `028` 为加法迁移，
且发布收尾时正式企划数为 0；紧急情况下可在确认仍无企划、部署身份和进行中命令后回滚
程序。创建首条企划或产生部署身份后，旧 API 不理解新合同，只允许前滚修复，不得删除
迁移、企划、审计或部署历史。数据库恢复必须使用同一时点备份并单独批准。

结构化证据见
[`evidence/API_0.30.0_PRODUCTION_DEPLOYMENT_2026-08-10.json`](evidence/API_0.30.0_PRODUCTION_DEPLOYMENT_2026-08-10.json)。

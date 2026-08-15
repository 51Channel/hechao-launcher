# API 0.31.0 正式发布

- 正式发布 ID：`0.31.0-20260815T024200Z`
- 源码提交：`8d765dbd39e6fe138d140f53b99c22bc8323df8b`
- 正式标签：`api-v0.31.0`
- 生产切换时间：2026-08-15 10:57（Asia/Shanghai）
- 数据库迁移：新增 `029_dynamic_deployment_slots.sql`，生产为 `29/29`

## 发布范围

- 整合包可选择固定 `activity` 或任一已就绪的 `activity-*` 动态部署槽；
- 完成 MFA 的管理员可在整合包导入页新建部署槽，API 只接受受控 ID、显示名、固定模板
  和原因，不接受任意路径、端口、任务名或命令；
- 新槽默认为停止、隐藏且未部署，代理完成创建并由心跳确认后才进入 `Ready`；
- 活动企划仍默认使用固定 `activity`，所有活动槽继续共享
  `127.0.0.1:25568 / owl5-activity-slot`，同一时刻最多运行一个；
- 创建和部署均不会自动启动 Minecraft。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.31.0-20260815T024200Z.tar.gz` | 46,275,788 字节 | `A639B12A570738000ECF64701E800AF4765B54E5679A665D4D846CD9DCB5E0A6` |
| `Hechao.Api` | 105,306,052 字节 | `1B735E050511AC94289ABF267700E656FE4F62F6428DFAACBE765C9AEB6601C6` |

归档共 `161` 项，不含 PDB、环境文件或凭据；本地、上传后和生产二进制哈希一致。

## 测试与备份

- API `337/337`、ServerControlAgent `64/64`、完整解决方案 `765/765`；
- Vitest `13/13`、Playwright `32/32`、PowerShell 7 脚本解析 `47/47`、发布溯源
  `20/20`，`git diff --check` 通过；
- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260815T025105Z.dump`，
  6,380,357 字节，SHA-256
  `56CA413211A9DA53E0C7CC656CEF51FE18578A8AA0C8F696C0EF42DE97476FB0`，
  `pg_restore --list` 读取 `239` 行；
- API release、环境、systemd 与 Nginx 配置快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.31.0-20260815T025105Z.tar.gz`，
  46,391,220 字节，SHA-256
  `8BA2A98F8E7C693067ADF950D5052814011CF831F9761D85565759CB7EF8B46B`。

## 生产验收

- API 原子切换到 `/opt/hechao-launcher-api/releases/0.31.0-20260815T024200Z`；
  PID `694836`、`NRestarts=0`，内外网健康与就绪均为 `200`、数据库为 `ready`；
- 迁移 `29/29`，`deployment_slots` 初始为 `0`，发布中整合包任务和待执行服控任务均为
  `0`；新建槽端点对未登录请求返回 `401`；
- 生产静态资源同时包含新建槽界面和 `/v1/admin/server-control/deployment-slots`；
- API 仍只监听 `127.0.0.1:8090`，公网 `8090` 不可连接；Publisher PID `2064` 与
  Nginx PID `1742715` 未变化，发布后 warning/error 为 `0`；
- `hechao.world`、`api.hechao.world`、`admin.hechao.world` 和启动器 API 均正常；
- owl5 `0.6.0` 心跳持续新鲜，七个既有目标未增删。活动 PID `3652`、大厅 PID `7328`
  保持原值，其余登记槽保持停止；没有发送 Minecraft 启停、重启或控制台命令。

发布门禁曾因两项验收脚本问题执行两次自动回滚：第一次把 `hechao.world` 的正常
`308` 跳转误判为失败，第二次是 Windows 回车污染远端脚本最后一行。两次均按预案恢复
`0.30.7` 并通过就绪检查；迁移 `029` 为事务化加法迁移，第一次切换后安全保留。最终改用
Base64 脚本传输并跟随站点跳转，全部门禁通过后完成正式切换。这两次回滚不是 API 或
数据库故障。

## 回滚

程序直接回滚目标为
`/opt/hechao-launcher-api/releases/0.30.7-20260814T144949Z`。迁移 `029` 为加法迁移，
程序回滚时保留表和审计记录，不删除已经创建的槽；如需停止新建功能，应回滚 API 并保留
owl5 代理 `0.6.0`，不得降级数据库或删除动态槽目录。

结构化证据见
[`evidence/API_0.31.0_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/API_0.31.0_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

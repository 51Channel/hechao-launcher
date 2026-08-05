# API 0.28.2 正式发布

- 正式发布 ID：`0.28.2-20260805T222544Z`
- 制品源码提交：`fc28e6d7858263b058b13e469f829f684409c556`
- 正式标签：`api-v0.28.2`
- 生产切换时间：2026-08-06 06:27:56 CST

## 修复范围

- 整合包“等待确认”表单把精确确认文本纳入本地草稿快照；
- 后台每 3 秒刷新任务、发布代理和活动部署槽状态时，不再清空管理员正在输入的确认文本；
- 确认文本输入后会正确显示“有未提交更改”，提交按钮状态保持稳定；
- 其他表单字段、任务刷新、修订冲突、关闭抽屉和切换任务的既有边界不变。

根因是草稿序列化遗漏 `confirmation` 字段。只编辑该字段时，页面错误地认为表单没有
修改，并在下一次轮询中重新初始化表单。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.28.2-20260805T222544Z.tar.gz` | 45,947,737 字节 | `07C41FDAE80103AC16A03CE10FBFE2D89332CB4EEC48B6500244654BD60F7E50` |
| `Hechao.Api` | 105,033,668 字节 | `94551BDF1296DFD7FB513004D10461C321619D24CA042605D607DB60E46F7DB7` |
| `chunk-PackageImportsView.js` | 27,628 字节 | `526A055A339AA5DCC520EBE123E7436354B91E3FFA93CF65543474F466E75A0D` |

归档共 158 项，解压后为 153 个文件；不含 PDB、环境文件、生产配置或凭据。上传后
VPS 端归档哈希和条目数与本机一致，部署后程序和整合包前端分块哈希也与本机构建一致。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260805T222643Z.dump`；
- 数据库 SHA-256：
  `E7BD819DB678DF5F310D71D9C2FAA09ED1B4DBA8E98F402BCFF9F7EEDFEAF826`；
- `pg_restore --list` 可读取 225 行目录记录；
- 当前 API、环境、systemd 与 Nginx 配置备份：
  `/var/backups/hechao-launcher/releases/0.28.2-20260805T222544Z`；
- 安装器原子切换 `current`，readiness 失败会自动恢复 API `0.28.1`；
- 只重启 `hechao-launcher-api.service`，没有向 Minecraft、Velocity、Publisher 或
  ServerControlAgent 发送控制命令。

## 验证

- TypeScript、Vite 生产构建通过，Vitest `8/8`；
- Playwright `16/16`；聚焦场景跨过至少一次真实 3 秒轮询，确认文本、脏数据提示和
  提交按钮状态均保持；
- API `282/282`、完整解决方案 `.NET 670/670`；
- Impeccable detector 无问题；
- 迁移保持 `24/24`，本版本没有新增数据库迁移；
- 本机与公网 `/healthz`、`/readyz` 均返回 `0.28.2`，数据库为 `ready`；
- 服务为 `active/enabled`、PID `803929`、`NRestarts=0`，发布后 warning/error 为 `0`；
- API 仍只监听 `127.0.0.1:8090`，公网直连超时；官网和旧中转 API 均为 `200`；
- NuGet 漏洞数据源在全解决方案测试时超时并产生 `NU1900` 警告，所有项目编译和测试
  仍成功；本次没有修改依赖或锁文件。

## 回滚

本版本的直接安全回滚目标为：

`/opt/hechao-launcher-api/releases/0.28.1-20260805T214936Z`

生产已经存在 `DeleteServerFiles` 操作记录，不得回滚到 `0.27.3`。回滚只切换
`current` 并重启 API；数据库迁移 024 和删除审计记录必须保留。

结构化证据见
[`evidence/PACKAGE_IMPORT_CONFIRMATION_POLLING_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/PACKAGE_IMPORT_CONFIRMATION_POLLING_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

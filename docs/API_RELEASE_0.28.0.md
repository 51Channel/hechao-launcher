# API 0.28.0 正式发布

- 正式发布 ID：`0.28.0-20260805T201046Z`
- 制品源码提交：`8423af7c451ad163c138d9de62954cfb43c4bd23`
- 正式标签：`api-v0.28.0`
- 生产切换时间：2026-08-06 04:18:11 CST

## 功能范围

- 管理后台服控面板新增“删除服务端文件”危险操作；
- 仅当代理在线、目标已停止、无进行中命令且代理显式开放能力时允许排队；
- 管理员必须填写 4 至 500 字符原因，并精确输入 `DELETE <serverId>`；
- 心跳和后台显示服务端文件是否存在及暂存清理状态；
- 文件删除后保留目标和审计记录，但禁用启动、重启和快捷设置。

API 只下发结构化目标 ID 和动作，不接收或执行路径、Shell 或 PowerShell 文本。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `0.28.0-20260805T201046Z.tar.gz` | 45,947,055 字节 | `B30C5C28F9A3890E46122D0ECF9D1697E6A24972D62968B99E67EA35D2E1DE94` |
| `Hechao.Api` | 105,033,668 字节 | `87DC3054EB2C91FA6E7060A3521588AA25A3E8182E25542F4919110D33838108` |

归档共 157 项，只包含 API、静态资源端点清单和 `wwwroot`，不含 PDB、环境文件或凭据。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260805T201409Z.dump`；
- 数据库 SHA-256：
  `D3042D5F383ACD605FB62E5264501BE4B8403C8871160DBB634DC096409F94F1`；
- `pg_restore --list` 可读取 210 个目录项；
- API、环境、systemd 与 Nginx 备份：
  `/var/backups/hechao-launcher/releases/20260805T201409Z`；
- 安装器原子切换 `current`，readiness 失败会恢复上一发布；
- 只重启 `hechao-launcher-api.service`，未操作 Minecraft、Velocity、Publisher 或 Nginx。

## 验证

- API `278/278`、完整解决方案 `666/666`；
- Vitest `8/8`、Playwright `15/15`，TypeScript 与前端生产构建通过；
- 迁移为 `24/24`，三个删除状态列存在；
- 本机与公网 `/healthz`、`/readyz` 均返回 `0.28.0`，数据库为 `ready`；
- 服务为 `active/running`、PID `719600`、`NRestarts=0`，发布后 warning/error 为 `0`；
- API 仍只监听 `127.0.0.1:8090`，公网直连超时；官网和旧中转 API 均为 `200`；
- 两个代理共上报 9 个目标、5 个删除能力位，9 个目录存在且无清理残留；
- 删除操作、进行中操作和进行中命令均为 `0`，本轮没有执行真实删除。

## 回滚

尚未产生 `DeleteServerFiles` 操作记录时，可切回
`/opt/hechao-launcher-api/releases/0.27.3-20260805T184018Z`；迁移 024 的加法列可保留。
一旦生产产生删除操作记录，`0.27.3` 无法识别新动作值，必须使用仍包含该合同的兼容
构建向前修复，不能直接二进制降级。

结构化证据见
[`evidence/SERVER_DIRECTORY_DELETION_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/SERVER_DIRECTORY_DELETION_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

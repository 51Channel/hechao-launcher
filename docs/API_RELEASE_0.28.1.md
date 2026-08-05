# API 0.28.1 正式发布

- 正式发布 ID：`0.28.1-20260805T214936Z`
- 制品源码提交：`e0974a63efd32bb8a9e5afdfe46391d39f0fc209`
- 正式标签：`api-v0.28.1`
- 生产切换时间：2026-08-06 05:52:50 CST

## 修复范围

- 服控概览不再显示目录已删除、后台清理已完成且没有进行中操作的目标；
- 目录正在移出、后台仍在清理或操作尚未结束时继续显示，避免管理员失去执行状态；
- 当前目标消失后，后台自动选择下一个可见目标，目标计数同步更新；
- 同一目标以后重新部署目录并恢复心跳时，会自动重新进入服控列表；
- 数据库目标、操作审计、代理配置、外置备份和 OSS 客户端均继续保留。

这次只修改服控概览的日常展示，不删除历史记录，也不改变服务端删除的安全边界。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.28.1-20260805T214936Z.tar.gz` | 45,947,724 字节 | `E831F14C4779A9C2146AAB3A1899661848AD303C4FDACE6D0FE2CA3E63A01C91` |
| `Hechao.Api` | 105,033,668 字节 | `C3771C9CF816D1BA091362FF7D67E76C63740EA19E9F29FBFCEFE43A15C583D3` |

归档共 158 项，解压后为 153 个文件；不含 PDB、环境文件、生产配置或凭据。上传后
VPS 端归档哈希和条目数与本机一致，部署后程序及管理前端哈希也与本机构建一致。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260805T215035Z.dump`；
- 数据库 SHA-256：
  `980B6CCEAE5F257F1BE874F0B5F51B1173E5049E74210F90543E57162A91385B`；
- `pg_restore --list` 可读取 225 行目录记录；
- 当前 API、环境、systemd 与 Nginx 配置备份：
  `/var/backups/hechao-launcher/releases/0.28.1-20260805T214936Z`；
- 安装器原子切换 `current`，readiness 失败会自动恢复 API `0.28.0`；
- 只重启 `hechao-launcher-api.service`，没有向 Minecraft、Velocity、Publisher 或
  ServerControlAgent 发送控制命令。

## 验证

- 完整解决方案 `.NET 670/670`、API `282/282`、ServerControlAgent `51/51`；
- Vitest `8/8`、Playwright `16/16`，TypeScript 与 Vite 生产构建通过；
- 迁移保持 `24/24`，本版本没有新增数据库迁移；
- 本机与公网 `/healthz`、`/readyz` 均返回 `0.28.1`，数据库为 `ready`；
- 服务为 `active/enabled`、PID `780568`、`NRestarts=0`，发布后 warning/error 为 `0`；
- API 仍只监听 `127.0.0.1:8090`，公网直连超时；官网和旧中转 API 均为 `200`；
- 生产数据库保留 9 个服控目标，其中 6 个符合日常显示条件；
- `activity`、`fanstreet`、`yugong` 均为目录不存在、清理完成、无活动操作，最近删除
  操作为 `Succeeded`，因此从服控列表隐藏；
- 管理前端 `admin.js` 的生产 SHA-256 与本机构建一致：
  `0773062D4C61E0A0FF8E9375717FFA9999B0A046B53469AA55C76B6FBD6FC925`。

## 回滚

生产已经存在 `DeleteServerFiles` 操作记录，不能回滚到不能识别该动作的 `0.27.3`。
本版本的直接安全回滚目标为：

`/opt/hechao-launcher-api/releases/0.28.0-20260805T201046Z`

回滚只切换 `current` 并重启 API；数据库迁移 024 和删除审计记录必须保留。

结构化证据见
[`evidence/SERVER_CONTROL_TARGET_VISIBILITY_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/SERVER_CONTROL_TARGET_VISIBILITY_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

# API 0.28.3 正式发布

- 正式发布 ID：`0.28.3-20260805T234331Z`
- 制品源码提交：`ec91f1734f46d5d9220d78e95a88513731ca75c1`
- 正式标签：`api-v0.28.3`
- 生产切换时间：2026-08-06 07:47:43 CST

## 修复范围

- 整合包“等待确认”页面检查活动部署槽时显式包含已删除服务端目录的目标；
- `activity` 目录删除后仍可读取保留的代理配置和部署能力，不再误报“服控代理”离线或
  “目标已停服”未满足；
- 普通服控面板继续隐藏目录不存在、清理完成且没有活动操作的目标；
- 服务端重新部署后仍按既有心跳和目录状态自动恢复到普通服控列表。

根因是整合包页与普通服控页共用同一个概览接口。API `0.28.1` 隐藏已完成删除目标后，
整合包页也无法读取仍可重新部署的固定活动槽，因此把目标缺失误判为代理离线。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.28.3-20260805T234331Z.tar.gz` | 45,947,709 字节 | `2AA0DC281C25E4E592DC4DC6DADFF0FE9E7D285E05E791F22CDC00B1C145D59A` |
| `Hechao.Api` | 105,034,180 字节 | `EBF43D83FD3D883464180C227D8B64701FC3FD851FBCFB48CF138EF86185DFB4` |
| `chunk-PackageImportsView.js` | 27,655 字节 | `A2DA6FDA70460A4BFE5EA2081ED8811EFCD3531010021B53558DDA16A9024A24` |

归档共 158 项，解压后为 153 个文件；不含 PDB、环境文件、生产配置或凭据。上传后
VPS 端归档哈希、部署后程序和公网整合包前端分块哈希均与本机构建一致。公网静态资源
包含 `includeDeletedTargets=true`，并返回 `Cache-Control: no-store`。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260805T234331Z.dump`；
- 数据库大小 3,438,009 字节，SHA-256：
  `03BF650D9C570057007F3507B0B245A4F00CBA23F87A203BB34C2CACF4790099`；
- `pg_restore --list` 可读取 225 行目录记录；
- 当前 API、环境、systemd 与 Nginx 配置备份：
  `/var/backups/hechao-launcher/releases/0.28.3-20260805T234331Z`；
- 安装器原子切换 `current`，readiness 或后置门禁失败会自动恢复 API `0.28.2`；
- 只重启 `hechao-launcher-api.service`，没有向 Minecraft、Velocity、Publisher 或
  ServerControlAgent 发送控制命令。

## 验证

- TypeScript、Vite 生产构建、Vitest `8/8` 和 Playwright `16/16` 通过；
- API `283/283`、完整解决方案 `.NET 671/671`；
- 聚焦浏览器回归确认已删除 `activity` 目标显示服控代理在线、目标已停服，并可提交部署；
- Impeccable detector 为零项，差异敏感信息扫描和 `git diff --check` 通过；
- 迁移保持 `24/24`，本版本没有新增数据库迁移；
- 生产数据库保留 9 个服控目标：普通概览为 6 个，整合包专用概览可读取全部 9 个；
- `activity` 由 owl5 代理 `0.4.0` 持续心跳，已停服、目录不存在、清理完成、允许部署，
  活动操作为 0；
- 本机和公网 `/healthz`、`/readyz` 均返回 `0.28.3`，数据库为 `ready`；
- 服务为 `active/enabled`、PID `855217`、`NRestarts=0`，部署后 warning/error 为 `0`；
- API 只监听 `127.0.0.1:8090`，公网直连超时；官网、旧中转 API 和后台均为 `200`，
  错误 Host 的后台路径为 `404`，无效 Bearer 为 `401`；
- Publisher PID `4028581` 在发布前后保持不变；未操作任何游戏服或代理；
- NuGet 漏洞数据源超时仅产生 `NU1900`，所有编译和测试成功，未修改依赖。

## 回滚

本版本的直接安全回滚目标为：

`/opt/hechao-launcher-api/releases/0.28.2-20260805T222544Z`

生产已经存在 `DeleteServerFiles` 操作记录，不得回滚到 `0.27.3`。回滚只切换
`current` 并重启 API；数据库迁移 024、删除审计记录和服控目标必须保留。

结构化证据见
[`evidence/PACKAGE_IMPORT_DELETED_TARGET_REDEPLOYMENT_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/PACKAGE_IMPORT_DELETED_TARGET_REDEPLOYMENT_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

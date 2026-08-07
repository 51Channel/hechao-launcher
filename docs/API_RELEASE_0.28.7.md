# API 0.28.7 正式发布

- 正式发布 ID：`0.28.7-20260807T072043Z`
- 制品源码提交：`af3c88d201338277e1fb505ab0006106b2458417`
- 正式标签：`api-v0.28.7`
- 生产切换时间：2026-08-07 15:33 CST
- 数据库迁移：无，当前仍为 `026`

## 修复范围

- 修复管理后台表单型抽屉被压缩到顶部的问题。打开的 Vue 抽屉使用三行 Grid，
  唯一子元素 `<form>` 此前只占据第一行，导致正文与页脚在约 `76px` 高度内重叠；
- 唯一表单现在跨越抽屉全部 Grid 行，表单内部继续使用固定页头、可滚动正文和固定页脚；
- 修复覆盖服务器新增/编辑和单服权限编辑等共享表单型抽屉，不改变目录、权限、排期、
  数据库或服控 API。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.28.7-20260807T072043Z.tar.gz` | 45,959,086 字节 | `2E0AD65E0A191E864AEB6C36D186A3E3257AF9095A57744E433C8502918C1F26` |
| `Hechao.Api` | 105,053,124 字节 | `09A30BA02CECF80E978B523D51E9596510373C829075E1F6C0923FF650790AE9` |
| `wwwroot/admin/assets/admin.css` | 59,516 字节 | `7589DAAC9CE125D210961D72DEDFB1817820B21CBCC7813B9F8DDAFA66162405` |

归档共 157 项、153 个文件，只有单文件 API、静态资源端点清单和 `wwwroot`；不含 PDB、
环境文件、生产配置或凭据。服务器端上传后重新计算的归档、安装器和 service 文件哈希
均与本地一致，归档路径检查未发现绝对路径或 `..` 条目。

## 备份与部署

- PostgreSQL custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260807T073101Z.dump`；
- 数据库备份大小 3,915,912 字节，SHA-256：
  `7DF7E457FC16ED8CE1030859B17818039DB22EB09ED3947E69565436DA9577A5`；
- 同名校验和与 `pg_restore --list` 均通过，可读取 225 行目录记录；
- 环境文件、systemd、Nginx 站点、旧 `current` 链接和旧二进制哈希备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.28.7-20260807T073138Z.tar.gz`；
- 配置备份大小 3,181 字节，SHA-256：
  `3BFE94FDB7C18A8E49F0C11FF7DF9FC318E2545A1DB2636BC83D911C3CA1A5D7`；
- 安装器原子切换 `current`，就绪门禁失败会自动恢复 API `0.28.6`；
- 本次只重启 `hechao-launcher-api.service`，没有重启或控制 Publisher、Nginx、
  Minecraft、Velocity 或两台 ServerControlAgent。

## 验证

- TypeScript、Vite 生产构建和 Release 全解决方案构建通过；
- Vitest `8/8`、Playwright `18/18`、API `.NET 289/289`、完整解决方案
  `.NET 695/695`；
- `1440x900` 下表单与抽屉等高，正文高度大于 `300px`，页头、正文和页脚不重叠；
- `390x844` 下滚动到公告和开关区域后，保存操作仍固定可见；
- 生产 `admin.css` 与候选制品 SHA-256 完全一致，且包含 `form:only-child` 修复选择器；
- API 服务 `active/running`，PID `2070277`、`NRestarts=0`，只监听
  `127.0.0.1:8090`；
- 回环与公网 `/healthz`、`/readyz` 均返回 `0.28.7` 和 `database=ready`；
- `hechao.world`、旧 `api.hechao.world` 和 `admin.hechao.world/admin/servers`
  均返回 200，公网 8090 不可连接；
- 部署后 warning/error 为 0；Publisher PID `1459607`、Nginx PID `459682`
  及两者 `NRestarts=0` 均未变化；
- 本次 `/tmp/hechao-api-release-0.28.7-20260807T072043Z` 上传目录已按固定绝对路径
  校验并精确清理，正式 release、回滚 release 和两份备份已再次复验；
- 现有浏览器管理员会话在生产目视复核时已过期，只显示“需要管理员身份”。因此没有
  把生产登录态点击验收写成已完成；该项待管理员从启动器重新进入后补验。

## 回滚

直接回滚目标为：

`/opt/hechao-launcher-api/releases/0.28.6-20260806T150509Z`

本版本没有数据库迁移，回滚只需原子恢复 `current` 并重启 API。生产已有
`DeleteServerFiles` 操作记录，仍不得回滚到无法识别该类型的 `0.27.3`。

结构化证据见
[`evidence/API_0.28.7_PRODUCTION_DEPLOYMENT_2026-08-07.json`](evidence/API_0.28.7_PRODUCTION_DEPLOYMENT_2026-08-07.json)。

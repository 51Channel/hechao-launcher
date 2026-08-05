# API 0.27.2 正式发布

- 正式发布 ID：`0.27.2-20260805T180006Z`
- 制品源码提交：`36b6c96589c25e9f22a918f933cea5beea6a9c3f`
- 正式标签：`api-v0.27.2`
- 生产切换时间：2026-08-06 02:03:28 CST

## 修复范围

管理后台“整合包导入”等页面的禁用按钮不再在鼠标移入时回退为白色背景。所有按钮
hover 规则只作用于 `:not(:disabled)`，禁用态保持原有背景、文字和透明度；生产 CSS
不再包含 `revert-layer`。

本版同时增加样式契约回归测试，并允许 Playwright 通过
`HECHAO_PLAYWRIGHT_EXECUTABLE_PATH` 使用系统浏览器。没有数据库迁移、API 契约或
游戏服配置变化。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `0.27.2-20260805T180006Z.tar.gz` | 45,939,173 字节 | `AE640434CB1F708A4DA94C68E3E3F2F4F971B72FB4C0E705518A1EEEDD767D55` |
| `Hechao.Api` | 105,020,356 字节 | `053FB17E85E0BAC4EC0C161BF4366287D9CB0F8BF89B96E8826BB141C51FEAA7` |

生产当前链接为
`/opt/hechao-launcher-api/releases/0.27.2-20260805T180006Z`，远端归档与二进制哈希
均和本机构建制品一致。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260805T180309Z.dump`；
- API 与配置备份：`/var/backups/hechao-launcher/releases/20260805T180303Z`；
- 原子安装脚本切换 `current`，就绪失败会自动恢复
  `0.27.1-20260804T211905Z`；
- 只重启 `hechao-launcher-api.service`，没有操作 Publisher、Nginx、Velocity 或任何
  Minecraft 服务端。

## 验证

- API 测试 `274/274`、Vitest `8/8`、Playwright `14/14`；
- 前端生产构建成功，Impeccable detector 为零问题；
- Edge 计算样式在 hover 前后保持一致：背景 `rgb(180, 35, 29)`、文字
  `rgb(255, 255, 255)`、透明度 `0.55`；
- 本机与公网 `/healthz`、`/readyz` 均返回 `0.27.2`，数据库为 `ready`；
- API 为 `active/running`、PID `630306`、`NRestarts=0`，发布后错误级 journal 为 `0`；
- Nginx PID 保持 `459682`、`NRestarts=0`；Publisher 保持发布前的
  `inactive/dead`；
- 公网 `admin.css` 包含 6 处 `:not(:disabled):hover`，不含 `revert-layer`。

## 回滚

本版没有 Schema 变化，直接回滚目标为
`/opt/hechao-launcher-api/releases/0.27.1-20260804T211905Z`。回滚只切换 API 的
`current` 链接并复核本机及公网健康，不需要回滚数据库，也不得修改 Minecraft 服务端。

结构化证据见
[`evidence/API_0.27.2_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/API_0.27.2_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

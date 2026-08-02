# API 0.26.2 正式发布

正式发布 ID：`0.26.2-20260802T093332Z`

源码提交：`b0b10140a3fb68b067987e2ddfc2f3b48ff682d5`

正式标签：`api-v0.26.2`

生产切换时间：2026-08-02 17:48:48 CST

## 修复范围

`0.26.2` 修复 Vue 管理后台在短窗口和长页面中的滚动边界：

- 根容器 `#app` 负责页面滚动，`body` 不再与应用容器争抢滚动区域；
- 桌面侧栏导航在高度不足时可以独立纵向滚动；
- 移动导航保持横向滚动，并禁止意外的纵向滚动；
- 新增 720px 高长玩家列表和 480px 高短窗口的浏览器回归测试，确认正文可到达底部、
  侧栏仍可用且移动导航不会覆盖正文。

本轮没有新增数据库迁移，没有修改认证、目录、服控接口或生产配置。API 版本从
`0.26.1` 升到 `0.26.2`，后台主脚本未变化，样式制品随滚动修复更新。

## 发布物

| 制品 | 字节 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.26.2-20260802T093332Z.tar.gz` | `45,790,504` | `00E3129F727E0EE022E34AF34CB7A0BF2983EC1BC7596C6D695EC1682DB322EF` |
| `Hechao.Api` | `104,653,363` | `38C9A7C8F09FAE7E871E815808EDB4F50C0AA108CD5D707DA5F067B6DB45DAA2` |

归档包含 `152` 个条目、`147` 个文件，其中后台静态文件 `145` 个。危险路径、链接、
重解析点、PDB、source map、前端源码、环境文件和凭据文件均为 `0`。

生产静态资源哈希：

- `admin.js`：`B37CD7A2DB189D00809D38F75EA9C283D9EC9D504934275E241B905B653E3C1D`
- `admin.css`：`1D4927E073ACD4264A8DB1DCB0C7B7B24A16BC7654E6358D94689C4B7992FBB4`

## 备份与部署

- 发布前数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260802T094602Z.dump`，
  `2,159,128` 字节，SHA-256
  `4A94625F75390A8C5BD7E5BD87D0C25916B19C9540FD181C15C2909E52461F8F`。
- 数据库备份通过同名旁车校验和与 `pg_restore --list`，目录项为 `207`。
- 完整配置快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.26.2-20260802T093332Z`，
  `19` 个文件全部通过 `SHA256SUMS`；该清单 SHA-256 为
  `796993D1B99B26E8691CCFE32169121B986A672B18982C8128652FE06C60D912`。
- 上传归档、安装脚本和 systemd 单元的远端哈希均与仓库一致；systemd 单元未变化。
- 原子安装脚本切换后等待 `/readyz`，本次一次成功；失败路径会恢复旧链接。

## 验证

- TypeScript：通过。
- Vitest：`8/8`。
- Playwright：`12/12`。
- API：`245/245`。
- 启动器：`202/202`。
- ServerControlAgent：`26/26`。
- 完整解决方案：`578/578`。
- 当前目录：`/opt/hechao-launcher-api/releases/0.26.2-20260802T093332Z`。
- 服务状态：`active/running`，PID `1781088`、`NRestarts=0`，只监听
  `127.0.0.1:8090`。
- `launcher-api.hechao.world` 与 `admin.hechao.world` 的 `/healthz`、`/readyz`
  均为 `200`，版本为 `0.26.2`，数据库为 `ready`，迁移为 `21/21`。
- 发布后错误级 journal 为 `0`。

最终复核时既有 Chrome 后台会话已经过期，因此没有把未登录页面冒充为生产登录态
目视验收。本轮 UI 修复由两个新增 Playwright 场景、`12/12` 浏览器回归、生产静态资源
哈希和公网健康检查覆盖；复核没有创建后台票据、修改目录或下发服控命令。

## 服控连续性

发布后两个代理均保持新鲜心跳：owl5 为 `0.2.4`、`7` 个目标、`3` 个运行；owl9
保持 `0.2.1`、`2` 个目标、`1` 个运行。总计 `9` 个目标、`2` 个在线代理、`4` 个
运行实例且无过期目标。自本发布 ID 起服控操作为 `0`，没有启动、停止、重启任何
Minecraft 服务端，也没有发送控制台命令。

## 回滚

直接回滚目标为
`/opt/hechao-launcher-api/releases/0.26.1-20260802T012527Z`，正式标签为
`api-v0.26.1`。[`install-release.sh`](../deploy/linux/install-release.sh) 会在新版本
就绪失败时自动恢复该链接。回滚保留迁移 `21`、MFA、会话、可信设备、审计和业务数据；
不删除任何表，也不要求重启 Minecraft 游戏服。

结构化证据见
[`evidence/API_0.26.2_PRODUCTION_DEPLOYMENT_2026-08-02.json`](evidence/API_0.26.2_PRODUCTION_DEPLOYMENT_2026-08-02.json)。

# API 0.26.1 正式发布

正式发布 ID：`0.26.1-20260802T012527Z`

源码提交：`3bfa13fbdc14acfdde551fc49150992824c0481f`

正式标签：`api-v0.26.1`

生产切换时间：2026-08-02 09:46 CST

## 功能范围

管理后台九个模块已经从单体原生 HTML、CSS、JavaScript 迁移到 Vue 3、TypeScript、
Vite 和 Vue Router。服务器、玩家、档案、运行数据、服务状态、服控、告警、诊断和
审计分别使用独立深层路由；ASP.NET Core 构建和发布会自动生成压缩后的生产静态资源。

本轮同时完成后台可靠性收口：页面读取相互隔离，轮询具备取消和代次保护，服控概览
不再携带全部服务器控制台，脏表单不被轮询覆盖，控制台保留阅读位置，危险操作使用
明确确认，审计显示前后差异，并补齐移动端与无障碍约束。

`0.26.0-20260802T010000Z` 曾短暂作为首个 Vue 生产版本运行。真实浏览器验收发现票据
已经被兑换后，Vue Router 仍可能把旧 fragment 再写回地址栏。`0.26.1` 在 Router 创建
前清除 fragment，并把票据保存在只消费一次的内存槽中。`api-v0.26.0` 保留为正式历史
标签，`api-v0.26.1` 是当前正式版本。

## 发布物

| 制品 | 字节 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.26.1-20260802T012527Z.tar.gz` | `45,790,438` | `85E7BF9EBE0DEFF27FA306CE1A3BE5E9327884F06EC2C00D9DEFE23FFC12F5F8` |
| `Hechao.Api` | `104,653,363` | `61D8E11F556FC215E52DE0295B106CC9C309F8CAB81ED283A5EE249B86C09DDF` |

归档包含 `152` 个条目、`147` 个文件，其中后台静态文件 `145` 个。危险路径、链接、
重解析点、PDB、source map、前端源码、环境文件和凭据文件均为 `0`；独立解压后的
主程序哈希与构建目录一致。

生产静态资源哈希：

- `admin.js`：`B37CD7A2DB189D00809D38F75EA9C283D9EC9D504934275E241B905B653E3C1D`
- `admin.css`：`4A5EC2596818395E3B8E3ADC5287BB97B9E736043654FFD07B496CDA095C18C5`

## 备份与部署

- 发布前数据库备份：`/var/backups/hechao-launcher/database/hechao-launcher-20260802T014221Z.dump`，`2,040,168` 字节，SHA-256 `E4431FE63A6639E7C0D0DD2D6DE0654316853BDAA6A12BB81CAE969085605FD5`。
- 数据库备份通过同名旁车 `sha256sum -c` 和 `pg_restore --list`，目录项为 `207`。
- 完整配置快照：`/var/backups/hechao-launcher/api-predeploy/pre-api-0.26.1-20260802T012527Z`，包含环境文件、systemd、Nginx、Data Protection key ring、档案清单、当前发布指针和运行状态；`19` 个文件全部通过 `SHA256SUMS`。
- 上传归档、安装脚本和 systemd 单元的远端哈希均与仓库一致；systemd 单元未变化。
- 原子安装脚本切换后等待 `/readyz`，本次一次成功；失败路径仍会自动恢复旧链接。

## 验证

- TypeScript：通过。
- Vitest：`8/8`。
- Playwright：`11/11`。
- API：`245/245`。
- 完整解决方案：`576/576`。
- 原子切换后当前目录为 `/opt/hechao-launcher-api/releases/0.26.1-20260802T012527Z`。
- 数据库迁移为 `21/21`；服务为 `active/running`，PID `1478889`、`NRestarts=0`，部署后错误级 journal 为 `0`。
- Kestrel 只监听 `127.0.0.1:8090`；本机与公网 `/healthz`、`/readyz` 均为 `200`，版本为 `0.26.1`，数据库为 `ready`。
- 九个 `/admin/*` 深层路由及主脚本、样式均为 `200`；匿名管理请求为 `401`，错误 Host `https://launcher-api.hechao.world/admin/` 为 `404`。

## 真实浏览器验收

2026-08-02 09:52 CST，从正式启动器创建新的 90 秒一次性管理员票据。生产可信设备
直接完成 MFA，最终地址为 `/admin/servers`，fragment 为空；数据库产生预期的票据创建、
后台会话创建和可信设备使用审计各 `1` 条，未留下有效待消费票据。

随后逐页等待异步数据稳定，再核对九个 Vue 页面。每页均有唯一正确标题、九项导航，
资源错误、骨架屏残留、横向溢出、破图和登录回退均为 `0`，浏览器 warning/error 为
`0`。服控和服务器目录最终画面没有控件重叠。验收没有执行新增、编辑、归档、告警确认、
权限修改、档案发布、服控或 Minecraft 命令。

只读控制面验收仍报告两条既有停服心跳 Critical、两条真正 PVP Warning，以及目录
强制登录尚未启用的外部门槛。这些状态未被确认、清除或通过启动游戏服掩盖，不属于
`0.26.1` 发布回归。

## 回滚

直接回滚目标为 `/opt/hechao-launcher-api/releases/0.26.0-20260802T010000Z`，其正式标签
为 `api-v0.26.0`。[`install-release.sh`](../deploy/linux/install-release.sh) 会在新版本
就绪失败时自动恢复该链接。回滚保留迁移 `21`、MFA、会话、可信设备、审计和业务数据；
不删除任何表。

配置恢复入口为
`/var/backups/hechao-launcher/api-predeploy/pre-api-0.26.1-20260802T012527Z`。回滚 API
不要求也不允许重启任何 Minecraft 游戏服。

结构化证据见
[`evidence/API_0.26.1_PRODUCTION_DEPLOYMENT_2026-08-02.json`](evidence/API_0.26.1_PRODUCTION_DEPLOYMENT_2026-08-02.json)。

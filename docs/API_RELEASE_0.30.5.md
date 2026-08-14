# API 0.30.5 正式发布

- 正式发布 ID：`0.30.5-20260814T121420Z`
- 制品源码提交：`54d3c0a1db57eb37097206bae338902da4b17a01`
- 正式标签：`api-v0.30.5`
- 生产切换时间：2026-08-14 20:19（Asia/Shanghai）
- 数据库迁移：无，保持 `028`

## 发布范围

本版修复新导入服务端在后台无法正确交接到服控面板的问题：

- 服务器目录明确区分玩家入口策略与 Java 进程状态，带服控目标的记录可以直达对应
  服务器的服控面板；
- 服控页使用 `/admin/control?server=<serverId>` 作为稳定深链接。目标不存在或已删除时，
  页面回退到首个可管理目标并显示告警；
- 立即部署的整合包完成后，详情页核对服控代理、运行目录、活动操作和部署任务身份，
  再提供“启动服务端”入口；
- 该入口只打开精确服控目标，不会自动提交启动命令。首次冷启动仍以服控操作历史和
  控制台结果为准。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.5-20260814T121420Z.tar.gz` | 46,251,141 字节 | `CA39C1C6E7D7CC124581E72DF2E3FE523D70F3E1C5226065A17F1B1361C6B496` |
| `Hechao.Api` | 105,245,636 字节 | `7532B2EFC276455486415181FC7E361527D3622D9ADF667BF89FD450E21AE4B8` |

归档共 `161` 项、`156` 个文件；生产二进制大小和 SHA-256 与正式制品一致。

## 测试与备份

- TypeScript 类型检查、Vitest `11/11`、Playwright `30/30`、API `.NET 315/315`、
  完整解决方案 `.NET 735/735`；
- Playwright 覆盖非默认目标深链接、无效目标回退、目录直达、部署交接和 `390px`
  移动端无横向溢出；
- 数据库 custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260814T121654Z.dump`，
  6,241,750 字节，SHA-256
  `A5DF8BB9478E9B547CC67895D63295BF1103CB959D94DA0D11EABD4FFBC6C86C`；
  `pg_restore --list` 成功读取 `239` 行；
- API 环境、systemd、Nginx 和完整 `0.30.4` release 快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.5-20260814T121654Z.tar.gz`，
  46,382,576 字节，SHA-256
  `2C7910B2A1A7EF768B32FC2B78DCF37254300789D512181B2AF639CAAFC40797`。

## 生产验收

- API 原子切换到 `/opt/hechao-launcher-api/releases/0.30.5-20260814T121420Z`；本机与
  公网 `/healthz`、`/readyz` 均为 `200`、版本 `0.30.5`、数据库 `ready`；
- API PID `225499`、`NRestarts=0`，只监听 `127.0.0.1:8090`；Publisher PID `2064`
  与 Nginx PID `1742715` 均未变化；
- 数据库迁移为 `28/28`，发布后 warning/error 为 `0`；公网 `8090` 保持关闭，
  管理后台、`api.hechao.world`、`hechao.world` 均返回 `200`；
- 管理后台正式静态文件包含目录直达、精确服控深链接和整合包部署交接三项修复；
- 整合包状态为 `Cancelled=3`、`Completed=3`、`Failed=3`、`Uploading=1`，服控操作为
  `Failed=11`、`Succeeded=17`；没有 `Pending`、`Running`、发布中或部署中任务；
- 本次没有数据库迁移、OSS 覆盖、Publisher/Nginx 重启或 Minecraft 服控命令。

## 回滚

程序直接回滚目标为
`/opt/hechao-launcher-api/releases/0.30.4-20260814T093000Z`。原子安装脚本在就绪检查
失败时会自动恢复该链接；迁移保持 `028`，不执行数据库降级。回滚不会撤销已经写入的
目录、整合包、服控或审计数据。

结构化证据见
[`evidence/API_0.30.5_PRODUCTION_DEPLOYMENT_2026-08-14.json`](evidence/API_0.30.5_PRODUCTION_DEPLOYMENT_2026-08-14.json)。

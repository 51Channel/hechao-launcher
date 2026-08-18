# API 0.34.0 后台经济监控与单品 K 线正式发布

- 正式发布 ID：`0.34.0-20260818T080552Z`
- 源码提交：`58a9c8f4ab139d7644f2ff58e70f31e8a7c1fe45`
- 正式标签：`api-v0.34.0`
- 生产切换时间：2026-08-18 16:12（Asia/Shanghai）
- 数据库迁移：`032_economy_dashboard_indexes`、`033_economy_item_history_index`
- 直接程序回滚目标：`0.33.1-20260817T031438Z`

## 功能范围

管理后台新增 `/admin/economy`。总体视图显示跨服共享货币供给、区间新增、转账额、财富
分布、玩家余额、官方回收和分服交易流量；服务器筛选只影响交易数据，不伪造单服余额。

单品视图通过只读接口 `/v1/admin/economy/items/history` 查询原版或模组商品，并按真实
已提交报价聚合小时或每日 OHLC。K 线上涨为红、下跌为绿，无成交时间桶保持空缺；页面
同时保留当前目录价、涨跌、成交数量、金额、卖家和笔数。当前没有玩家自由市场，因此
页面统一称为“官方回收行情”。

## 制品与备份

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.34.0-20260818T080552Z-linux-x64.tar.gz` | 46,963,626 字节 | `984FBEBA97210C547B94BBE7B6AA2397D7AD4DB8F10E16BA1CE341A10B97C5CE` |
| `Hechao.Api` | 105,544,260 字节 | `49947B0654724C28A24E8694B1EDFEDB48F88355CC14E5BA1D0870C2D3625284` |

归档共 `164` 项、`159` 个文件，不含 PDB、环境文件、凭据或危险路径。生产二进制与本地
构建原件的大小和 SHA-256 一致。

- PostgreSQL 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.34.0-20260818T081042Z.dump`，
  `7,099,817` 字节，SHA-256
  `B791F3442205A607772143F017432541E595D9223EE2DBF894D515E1FFB31CAD`；
  `pg_restore --list` 为 `285` 项。
- API、环境、systemd、Nginx 与 Data Protection key ring 备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.34.0-20260818T081042Z.tar.gz`，
  `46,457,082` 字节，SHA-256
  `45F6418137421033BD3DCB524A8BA84AEA5FB0F68D62B986B57023A205ABE0FE`。

两份备份均留在生产主机并保持 root 权限；发布记录不包含环境内容或任何凭据。

## 测试与生产验收

- 完整 .NET 解决方案 `815/815`，API `368/368`；另有 `1` 条隔离 PostgreSQL 条件测试
  因本机未配置独立测试库而跳过，未对正式账本写入测试成交；
- Vitest `19/19`、Playwright `34/34`，十二路由 WCAG A/AA 与桌面/手机 K 线视觉验收通过；
- 原子切换到 `/opt/hechao-launcher-api/releases/0.34.0-20260818T080552Z`，最终 API PID
  `3110051`、`NRestarts=0`；本机与公网健康、就绪均返回 `0.34.0` 和数据库 `ready`；
- 迁移为 `33/33`，四个分析索引全部存在，商品目录保持 `85/85`；
- `/admin/economy` 和 K 线静态资源返回 `200`，两个管理员数据接口的匿名请求返回 `401`；
- 生产当前经济账户、操作、报价和已提交报价均为 `0`，页面按真实数据显示空行情，不生成
  演示蜡烛；首批真实回收后的有数据目视验收仍待完成；
- 发布后 warning 及以上 API 日志为 `0`，公网 `8090` 继续不可连接；官网、中转 API、
  管理域名均为 `200`；Publisher PID `2064` 与 Nginx PID `1742715` 未变化。

本次只重启 `hechao-launcher-api.service`。没有向 Minecraft、Velocity、Publisher、Nginx
或服控代理发送启停、重启或控制台命令。

## 回滚

标准安装脚本在新版本未就绪时会自动恢复旧链接。手工直接程序回滚目标为
`/opt/hechao-launcher-api/releases/0.33.1-20260817T031438Z`；迁移 032、033 只增加索引，
程序回滚时保留，不删除迁移记录，也不改动迁移 031 的经济数据。

结构化证据见
[`evidence/API_0.34.0_PRODUCTION_DEPLOYMENT_2026-08-18.json`](evidence/API_0.34.0_PRODUCTION_DEPLOYMENT_2026-08-18.json)。

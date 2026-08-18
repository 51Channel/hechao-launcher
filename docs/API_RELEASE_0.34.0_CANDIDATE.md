# API 0.34.0 后台经济监控与单品 K 线候选

- 候选日期：2026-08-18
- 数据库迁移：`032_economy_dashboard_indexes`、`033_economy_item_history_index`
- 直接程序回滚目标：API `0.33.1-20260817T031438Z`
- 生产状态：尚未部署

## 功能范围

管理后台新增 `/admin/economy`，展示跨服货币供给、区间新增、转账额、财富分布、
玩家余额、官方物资回收和分服交易流量。总货币量始终按共享钱包统计；服务器筛选只
影响交易数据，不把共享余额错误拆成单服余额。

单品页通过只读接口 `/v1/admin/economy/items/history` 查询当前 85 项官方回收目录中的
原版或模组物品，并按真实已提交报价聚合小时或每日 OHLC。页面使用上涨红、下跌绿的
K 线，保留成交数量、金额、卖家数和笔数；无成交时间桶留空。当前没有玩家自由市场，
因此页面统一称为“官方回收行情”，不称作玩家市场价。

## 数据库边界

迁移 032 和 033 只增加分析查询索引，不修改迁移 031 的账户、账本、报价、商品或审计
写入合同。程序回滚时保留新索引和既有经济数据，不做数据库降级。

## 发布门

- 完整 .NET 解决方案、API、Vitest、Playwright 和前端构建全部通过；
- PostgreSQL custom-format 备份通过 SHA-256 与 `pg_restore --list`；
- API、环境、systemd、Nginx、Data Protection key ring 和当前发布链接完成备份；
- 原子发布后迁移必须为 `33/33`，本机及公网健康和就绪端点必须通过；
- 经济总览、单品搜索与 K 线接口必须返回生产真实数据，商品目录仍为 `85/85`；
- Publisher、Nginx、Minecraft、Velocity 和服控代理 PID 不得因本次发布变化。

## 部署边界

发布只允许使用 `install-release.sh` 原子替换并重启
`hechao-launcher-api.service`。不得启动、停止或重启 Publisher、Nginx、Velocity、
Minecraft 或服控代理。新版本就绪、迁移、经济接口或旧业务回归任一失败时，立即恢复
到 `0.33.1-20260817T031438Z`。

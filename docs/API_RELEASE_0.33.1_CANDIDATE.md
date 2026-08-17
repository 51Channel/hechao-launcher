# API 0.33.1 经济商品目录热修候选

- 候选日期：2026-08-17
- 数据库迁移：无，继续使用 `031_economy_ledger`
- 直接程序回滚目标：API `0.33.0-20260817T022304Z`
- 生产状态：尚未部署

## 根因与修复

`0.33.0` 的 `ListProductsAsync(false)` 在拼接 PostgreSQL 查询时没有在表名和
`WHERE enabled` 之间保留空白，生产实际执行为
`FROM launcher.economy_productsWHERE enabled`。PostgreSQL 因而把
`economy_productswhere` 当作表名并返回 `42P01`，游戏服的 `/shop`、商品管理和出售入口
无法取得目录。

`0.33.1` 使用完整 SQL 片段拼接，默认目录只返回启用商品，管理目录可包含停用商品。
真实 PostgreSQL 集成测试新增以下回归覆盖：

- 两件启用商品按物品 ID 排序返回；
- 停用一件商品后，默认目录只返回仍启用的一件；
- `includeDisabled=true` 仍返回两件，并保留停用状态。

## 候选验证

- API：`359` 通过、`1` 条件测试跳过；
- 完整 .NET 解决方案：`799` 通过、`1` 条件测试跳过；
- 真实隔离 PostgreSQL：`1/1` 通过，临时数据库和角色已清理；
- HechaoEconomy：`18/18`；
- `git diff --check`：通过。

## 部署边界

本次没有数据库迁移，也不修改经济令牌、账本、商品、报价或审计数据。发布只允许原子
替换并重启 `hechao-launcher-api.service`，不得操作 Publisher、Nginx、Velocity 或其他
Minecraft 服务端。公网健康、就绪和带服务身份的商品目录探针任一失败时，立即回滚到
`0.33.0-20260817T022304Z`。

# API 0.31.1 候选

- 状态：`NOT_DEPLOYED`
- 日期：2026-08-14
- 数据库迁移：`029_economy_ledger.sql`，相对 `0.31.0` 无新增迁移

## 范围

- 保留 `0.31.0` 的权威 Economy Service 和通用受控整合包部署目标。
- 经济商品 ID 从只允许 `minecraft:` 扩展为合法小写命名空间，支持 Create 等模组的
  普通无自定义数据物品。
- 新增 `PUT /v1/internal/economy/products?itemId=...` 和
  `POST /v1/internal/economy/products/disable?itemId=...`，安全支持物品路径中的 `/`；旧路径
  端点保留兼容。
- Bukkit 插件仍在提交前拒绝命名、附魔、容器、带数据组件和其他元数据物品，API 只接受
  已由服主显式加入的商品 ID。

## 验证与发布边界

聚焦 Economy API 规则 `5/5`、HechaoEconomy `0.1.2` 的 `10/10`、
HechaoEconomyScreen `0.1.2` 的 `3/3` 和一键包 `1.0.5` 双校验已通过。完整 API
`312/312`、完整解决方案 `731/731` 通过，API Release 构建零警告零错误。完整结果以
[`SKYREALM_ECONOMY_INTEGRATION_1.0.5.md`](SKYREALM_ECONOMY_INTEGRATION_1.0.5.md)
为准。

候选未部署、未应用迁移、未修改生产环境变量、未上传整合包，也未控制 Minecraft、
Velocity、Publisher 或服控 Agent。生产发布必须继续按 `0.31.0` 的备份、隔离迁移、原子
release 和失败回滚门禁执行。

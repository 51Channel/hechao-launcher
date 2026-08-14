# API 0.31.0 候选

- 状态：`NOT_DEPLOYED`
- 日期：2026-08-14
- 数据库迁移：`029_economy_ledger.sql`

## 范围

- 新增 Launcher API Economy Service：PostgreSQL 双式账本、幂等转账、出售报价与确认、
  商品目录、额度和审计。
- 整合包确认、部署编排、服务端归档 Range 授权和目录同步统一使用通用受控目标规则：
  只接受显式 `packageDeploymentEnabled=true`、合法 server ID/Agent ID/端口的目标。
- 多目标后台不预选，部署精确确认包含目标 ID。目录同步使用所选目标 ID 作为 Velocity
  目标。
- 活动企划继续使用严格 `activity/owl5/25568/owl5-activity-slot` 规则。

## 验证与发布边界

聚焦 API 规则与合同 `17/17`、完整 API `311/311`、完整解决方案 `730/730`、管理后台
Vitest `11/11`、Playwright `26/26` 已通过；Release 构建为 `0` 警告、`0` 错误。

候选未部署、未应用迁移、未修改生产环境变量、未上传整合包，也未控制 Minecraft、
Velocity、Publisher 或服控 Agent。生产发布必须先备份数据库和 API release，隔离验证
迁移，再按原子 release 门禁前滚；失败自动恢复 `0.30.2` 程序，迁移只前滚修复。

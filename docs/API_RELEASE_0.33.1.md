# API 0.33.1 经济商品目录热修发布

- 发布日期：2026-08-17
- 发布 ID：`0.33.1-20260817T031438Z`
- 源码提交：`5febdd0`
- 正式标签：`api-v0.33.1`
- 数据库迁移：无新增，保持 `31/31`
- 直接回滚目标：`0.33.0-20260817T022304Z`

## 生产缺陷

API `0.33.0` 的启用商品查询缺少 SQL 片段间空白，PostgreSQL 将
`launcher.economy_productsWHERE` 解析为不存在的 `economy_productswhere` 表并返回
`42P01`。余额接口不受影响，但 `/shop`、出售报价前的目录和服主商品维护无法使用。

`0.33.1` 使用可单测的完整查询片段，并同时覆盖仅启用商品与包含停用商品两条路径。

## 制品与备份

- 发布归档：`hechao-api-0.33.1-20260817T031438Z-linux-x64.tar.gz`；
- 归档大小：`46,320,082` 字节；
- 归档 SHA-256：`C98C0A77DDDB400B706F20E5F951D1B020B79CCA89C251C89945E8335543A2E3`；
- 生产二进制大小：`105,437,636` 字节；
- 生产二进制 SHA-256：`6FB74C1B081DF8BB42EC0E0E7B438AA596A5386E317EA197AA135BFA63529827`；
- PostgreSQL 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.33.1-20260817T031556Z.dump`，
  `6,862,270` 字节，SHA-256
  `2BB68ED9982EAC651EAE5CCB36450EA95A3E5728DBF9453C757568029AC440B0`，
  `pg_restore --list` 为 `285` 行；
- API、环境、systemd 与 Nginx 备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.33.1-20260817T031556Z.tar.gz`，
  `46,450,282` 字节，SHA-256
  `B8F8BD6EC85029E3329DA3E219649CA1798B9A01953F6CAFE9458270B87EA37A`。

备份位于生产主机且保持 root 权限，发布记录不包含环境文件内容或任何凭据。

## 验证

- API：`359` 通过、`1` 条件测试跳过；
- 完整 .NET 解决方案：`799` 通过、`1` 条件测试跳过；
- 真实隔离 PostgreSQL：`1/1`；
- 生产 `MainPID=2215592`、`NRestarts=0`、`active/running`；
- 本机与公网 `/healthz`、`/readyz` 均返回 `0.33.1` 和数据库 `ready`；
- owl5 使用外置经济服务身份请求启用商品目录返回 `200`，当前商品数为 `0`；
- 迁移记录为 `31/31`，发布后 warning 及以上日志为 `0`；
- Publisher `PID 2064` 与 Nginx `PID 1742715` 均保持 `active/running`、`NRestarts=0`；
- `hechao.world` 与 `api.hechao.world` 最终均为 `200`；
- 临时上传文件已清理。

本次只重启 API，没有重启 Publisher、Nginx、Velocity 或其他 Minecraft 服务。工业季只为
独立组件验收临时冷启动，最终保持停止。

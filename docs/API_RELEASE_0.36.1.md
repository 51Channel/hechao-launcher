# API 0.36.1 官方回收部分额度正式发布

- 正式发布 ID：`0.36.1-20260821T083823Z`
- 发布日期：`2026-08-21`
- 源码提交：`9b00be37100548274da611454f1c59a791dfdf27`
- 正式标签：`api-v0.36.1`
- 数据库迁移：无，保持 `34/34`
- 直接程序回滚目标：`0.36.0-20260820T145340Z`

## 功能范围

官方回收报价数量改为请求数量、个人剩余额度和全服剩余额度三者最小值。只要仍有至少
`1` 个额度，API 就创建部分报价并按实际报价数量计算总额及剩余额度；额度为 `0` 时继续
返回准确的个人或全服额度错误。商品不存在、商品暂停、报价过期、提交幂等和账本事务语义
均未改变。

本合同必须与 HechaoEconomy `0.2.3` 协调使用。旧插件 `0.2.2` 要求输入槽数量等于报价
数量，不能单独接收部分报价。本版没有修改 Screen、客户端档案、数据库结构或发布通道。

## 制品与备份

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.36.1-20260821T083823Z-linux-x64.tar.gz` | 46,989,572 字节 | `1333B9AB4CE402141EBC258AA5E9FF90DBD9AE2C8937B59F39E6CFF1667731C7` |
| `Hechao.Api` | 105,646,660 字节 | `999FD3AE22748906781DA0977101B60495BEAAA3B6EE53EBEF811EFDEC4E9CDA` |

发布前备份为：

- 数据库：`/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.36.1-20260821T084804Z.dump`，
  `7,720,856` 字节，SHA-256
  `D83CFB227642D8306208CCC35644A0DC68B5179783C5F8A578ACD8232AEAA2DF`，
  `pg_restore --list` 为 `304` 行；
- API 精确配置快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.36.1-20260821T084804Z-precise.tar.gz`，
  `62,365,043` 字节，SHA-256
  `5ECFD07BD114D1CE981DF9B39C7A2E388B58E4C44E7C613AD66F41305856326D`。

精确快照包含当前 API 发布、环境、systemd、Nginx、Data Protection、清单和诊断目录，
不包含 `5.9 GiB` Publisher/package-imports 工作空间。误含该工作空间的冗余归档已在精确
快照验证后删除，根盘可用空间恢复到 `10,046,111,744` 字节。

## 验证

- 完整 .NET 解决方案 `826` 通过、失败 `0`，常规环境只跳过隔离 PostgreSQL `1` 项；
- 临时 PostgreSQL 真实迁移与交易 `1/1` 通过，测试数据库和角色已清理；
- 当前链接为 `/opt/hechao-launcher-api/releases/0.36.1-20260821T083823Z`，远端二进制
  大小和 SHA-256 与本地原件一致；
- 最终 PID `525035`、`NRestarts=0`，本机和公网健康为 `ok / 0.36.1`，就绪和数据库为
  `ready`，发布窗口 warning 及以上日志为 `0`；
- 数据库迁移 `34/34`，商品 `85/85` 启用；
- 生产服务令牌烟测中，请求 `64` 个苹果返回报价数量 `32`、总额 `64.00`、个人剩余
  `0`、全服剩余 `608`。测试报价精准删除 `1` 条、残留 `0`，报价表最终为 `0`；
- Nginx 和 Publisher 未重启，最终均为 `NRestarts=0`。

## 回滚

程序故障时把 API 原子切回
`/opt/hechao-launcher-api/releases/0.36.0-20260820T145340Z`，同时把目标服插件恢复为
HechaoEconomy `0.2.2`，然后只重启 API 与 `activity-survival`。本版没有数据库迁移，
不执行数据库降级或删除任何账本、报价和市场数据。

结构化证据见
[`evidence/API_0.36.1_PRODUCTION_DEPLOYMENT_2026-08-21.json`](evidence/API_0.36.1_PRODUCTION_DEPLOYMENT_2026-08-21.json)。

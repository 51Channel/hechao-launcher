# API 0.35.0 玩家市场正式发布

- 正式发布 ID：`0.35.0-20260818T121031Z`
- 发布日期：`2026-08-18`
- 源码提交：`426b23fa98fdc9d76c97b2006c50da0fe315a00c`
- 功能提交：`e3df1a2`
- 正式标签：`api-v0.35.0`
- 数据库迁移：`034_economy_player_market`
- 直接程序回滚目标：`0.34.0-20260818T080552Z`

## 功能范围

API 新增按 `serverId` 隔离的玩家市场。上架、购买、下架和领取均要求幂等键；购买在同一
PostgreSQL 事务内完成买家扣款、卖家到账、成交税、挂单状态和待领取写入。到期挂单在
市场、个人挂单和待领取查询前统一结算，物品不直接掉落到世界。

默认规则为 `1%` 上架费且最低 `1.00`、`3%` 成交税、`24h` 有效期和每人最多 `5` 个
活动挂单。当前只托管通过服务端普通物品策略的 `itemId + quantity`；命名、附魔、容器、
耐久和其他复杂 Data Components/NBT 明确不在本版范围内。

## 制品与备份

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.35.0-20260818T121031Z-linux-x64.tar.gz` | 46,966,874 字节 | `CF2E845F26F305F58D26020722E24318E7A953558A18D84858396F54DB8B5FD3` |
| `Hechao.Api` | 105,636,292 字节 | `B9462B7D5AF6FA662408574E078008B03029F269EF7D9DDCC5AE17DBCC512AB8` |

生产数据库和 API 配置备份分别位于：

- `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.35.0-20260818T121434Z.dump`
  （7,131,806 字节，SHA-256
  `17624F8D12794DA44710BD4FF46A64EFF4D510541773260CD86C94C7796AF83D`）；
- `/var/backups/hechao-launcher/api-predeploy/pre-api-0.35.0-20260818T121434Z.tar.gz`
  （58,118,340 字节，SHA-256
  `69229ECDEFF0EE652334BFE11B71CDA945E4B645EF28FBC7779FEA59131CDD0F`）。

两份备份均为 `root:root / 0600`，发布记录不包含环境内容或凭据。

## 测试与生产验收

- 完整 .NET 方案 `819/819`、API `372/372`；常规环境跳过的隔离 PostgreSQL 用例已在
  独立 PostgreSQL 中单独通过 `1/1`；
- 生产迁移为 `34/34`，两张玩家市场表均存在；
- 当前链接为 `/opt/hechao-launcher-api/releases/0.35.0-20260818T121031Z`，生产二进制
  大小和摘要与本地制品一致；
- 复核时 PID `3236646`、`NRestarts=0`，健康为 `ok`，就绪和数据库均为 `ready`；
- 玩家市场匿名接口返回 `401`，最近 20 分钟 warning 及以上日志为 `0`。

本次 API 发布只重启 `hechao-launcher-api.service`，没有操作 Minecraft、Velocity、
Publisher、Nginx 或服控代理。真人双账号交易仍属于 Test 阶段验收，不因程序上线而视为
已经通过。

## 回滚

程序故障时原子切回
`/opt/hechao-launcher-api/releases/0.34.0-20260818T080552Z` 并只重启 API。迁移 034
和已经产生的市场审计、挂单及待领取记录必须保留，不执行数据库降级或删表。

结构化证据见
[`evidence/API_0.35.0_PRODUCTION_DEPLOYMENT_2026-08-18.json`](evidence/API_0.35.0_PRODUCTION_DEPLOYMENT_2026-08-18.json)。

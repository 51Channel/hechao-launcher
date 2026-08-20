# 天域远征工业季玩家市场 0.2.2 候选

- 候选日期：`2026-08-20`
- 状态：本地候选，未上传 OSS、未导入后台、未部署 API、插件或客户端档案
- API 源码版本：`0.36.0`
- 服务端插件：`HechaoEconomy-0.2.2.jar`
- 客户端模组：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.7.jar`
- 数据库迁移：无新增迁移，兼容既有 `034_economy_player_market`

## 本轮功能

本轮按 DonutSMP 的 Auction House 交互研究，先把玩家市场的读取排序和单位价信息做成一个
完整切片。挂单写入、购买、成交税、下架、到期、待领取、幂等键和 `serverId` 隔离均未改变。

API `GET /v1/internal/economy/market/listings` 新增白名单参数 `sort`：

- `recently_listed`：最新上架，按 `created_at DESC, listing_id` 稳定排序；
- `lowest_unit_price`：单位价从低到高，按精确的 `total_price / quantity` 排序；
- `highest_unit_price`：单位价从高到低；
- `expiring_soon`：剩余时间从短到长。

省略 `sort` 或传空值仍使用 `recently_listed`。未知值返回验证错误，排序表达式只来自服务端
枚举，不拼接客户端原始输入。响应新增计算字段 `unitPrice`，以总价除以数量并四舍五入到
四位小数；原有字段和写入合同保持不变。

## 游戏内交互

- Bukkit 市场菜单底部空槽 `51` 变为排序控制，点击后循环四种排序并异步刷新；刷新期间按钮
  显示处理中状态，失败会恢复上一种排序，不重复发送请求；
- 挂单 Lore 同时显示总价和单位价，完整卖家、数量、剩余时间和操作提示仍保留；
- NeoForge Screen 在标题栏搜索框旁显示紧凑排序按钮，窄窗口会收缩搜索框而不是重叠；
- 市场卡片在空间足够时显示总价和单位价两行，原生 Tooltip 仍可查看完整 Lore；
- 旧客户端命令、旧 API 请求和省略排序参数的旧插件调用继续默认使用最新上架。

## 本地验证

- API 完整测试：通过 `375`，跳过 PostgreSQL 条件集成测试 `1` 条（本机未配置隔离数据库），
  失败 `0`；
- HechaoEconomy：PowerShell 7 + Java 21 执行 `clean test build --no-daemon`，`30/30`
  测试和构建通过；JAR `443,345` 字节，SHA-256
  `37A91D49FEBFC2B90723E0E12DE2E36AFEA5DF835241AD0272D107C6D34EFEEF`；
- Screen：PowerShell 7 + Java 21 执行 `clean test build --no-daemon`，`85/85`
  测试和构建通过；重复执行 `jar --no-daemon` 后制品保持不变；JAR `935,775` 字节，
  SHA-256 `1F991341DF61F610DFB49D21D8CED3FA33B01C689E856315F4C8CE4D85B807EF`；
- `git diff --check`：通过。

## 上线前门禁

1. 在隔离 PostgreSQL 运行迁移 `001-034` 和排序读取、单位价 JSON、市场事务全套测试；
2. 用两个测试账号确认最新、低价、高价、临期四种排序在搜索、分页和重复点击下稳定；
3. 在 `512 x 270`、`200 x 140` 和高 DPI 客户端中完成真实 Screen 目视检查，确认搜索框、
   排序按钮、物品卡和 Tooltip 不重叠；
4. 再按既有流程制作新的不可变客户端档案，只推进 Test，保留 `1.0.23` 作为回滚目标；
5. 完成双账号购买、下架、待领取、断线、背包竞争、幂等重试和余额守恒后，才评估 Gray。

本候选不启动或重启任何生产服务，不上传 OSS，不修改生产数据库，也不把成交历史、订单簿、
真正 `/shop` 或 shards 统计宣称为已实现。

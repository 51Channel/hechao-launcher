# 天域远征工业季经济集成 1.0.2

> 状态：已完成源码、自动测试、离线打包和后台分析器验收；未部署 API、未执行生产数据库
> 迁移、未配置生产令牌，也未启动任何 Minecraft 服务端。
> 日期：2026-08-14
> 输入：`E:\生存服 交接包（客户端服务端）.zip`

## 1. 实现范围

| 组件 | 版本 | 当前职责 |
| --- | --- | --- |
| Launcher API Economy Service | 源码候选 | PostgreSQL 双式账本、幂等转账、出售报价与确认、商品目录、每日额度和审计 |
| HechaoEconomy | `0.1.1` | `/money`、`/pay`、`/sell`、`/shop`、商品管理、Vault 只读接入和 PlaceholderAPI |
| HechaoEconomyScreen | `0.1.0` | Minecraft `1.21.1` / NeoForge `21.1.228` 双端全屏入口与服务端权威会话 |
| ModpackInspector | 源码候选 | 使用后台同一分析器识别标准导入包并拆分客户端、服务端 |

数据库迁移为 `029_economy_ledger.sql`。余额只以正版 UUID 和 PostgreSQL 为权威，插件
不创建本地余额。所有内部接口位于 `/v1/internal/economy`，要求 HTTPS、Bearer 令牌、
`X-Hechao-Server-Id` 白名单和固定时间哈希比较；服务未配置或身份不合法时故障关闭。

## 2. 插件行为

- `/money` 查询余额；网络失败时只允许显示带时效的缓存，不把缓存当作可写权威。
- `/pay` 使用幂等操作 ID；达到阈值后要求二次确认。
- `/sell` 先签发短期报价，确认时重新核验主手物品；只接受无名称、无附魔、无额外数据
  组件的原版白名单物品。
- 提交结果不确定时，已移除物品写入 `quarantined-sales.yml` 隔离记录，不直接返还造成
  复制；该记录仍需运维人工核对账本后处理。
- `/shop` 显示 API 商品目录；管理员可持物使用 `/heco product set/remove` 配置商品。
- Vault 以最高优先级注册，并在服务端完全加载后核验最终所有者；发生所有权冲突时关闭
  新交易。
- PlaceholderAPI 提供 `%hechao_balance%`，一键包中的 TAB 已改用该占位符。
- 配置重新加载失败时，`0.1.1` 会立即丢弃旧网关并切换到不可交易状态，不继续沿用旧
  凭据。

## 3. 明确未实现的能力

- Vault `depositPlayer`、`withdrawPlayer` 和银行接口故意返回 `NOT_IMPLEMENTED`。未经赫朝
  审计接口，任何第三方插件都不能直接改余额。
- GriefPrevention 的付费领地尚未接入中央账本。正式接入前不得把领地购买或扩容配置成
  依赖 Vault 自动扣款，否则会失败而不是偷偷写入 Essentials 余额。
- 玩家拍卖、收购订单、经济邮箱、价格历史、启动器余额展示和后台经济管理页不属于本次
  首版。
- Create、Sable 和其他模组物品仍默认禁售；客户端菜单只负责受控导航，不赋予客户端
  交易权威。

## 4. 冲突处理

一键包禁用了 Essentials 的 `balance`、`pay`、`sell`、`worth`、`eco` 等经济命令，
并用说明文件替换 `worth.yml`。TAB 使用赫朝占位符。包内没有经济令牌，也删除了旧的
LuckPerms H2、SkyrealmCore SQLite、本机 PCL 和本机启动脚本。

HechaoEconomy 不修改 SkyrealmCore 的设置、TPA 和队伍职责。LuckPerms 仍只提供权限，
不能保存余额。长期生存服需要独立的受控部署目标，不能部署到单活动 `activity` 槽。

## 5. 一键包与离线验收

自动验证结果：完整 `.NET` 解决方案 `730/730`、API `310/310`、Bukkit 插件 `8/8`、
NeoForge 菜单 `2/2`；API Release 构建为 `0` 警告、`0` 错误。两个 Java 组件均使用
Java `21` 完成干净构建，重建后的 SHA-256 与包内制品一致。

最终候选包：

- 路径：`E:\天域远征工业季-赫朝一键导入-1.0.2.zip`
- 大小：`1,585,997,207` 字节
- SHA-256：`931F4FED5EFF5F02A17F845162E6CEF8D3E4CC2D0460E6FDD8886D6FA54BF9B8`
- 载荷：`4,798` 个文件，`1,584,232,265` 字节
- HechaoEconomy `0.1.1` SHA-256：
  `1EFC1AE4BD1E935B1A4BC3A4D51069F94DDD7B3A39BDA10524FC78EC0F6C4DEA`
- 双端菜单 `0.1.0` SHA-256：
  `B18958E3A30698D6AC2618662BC36D48A103D51EB2A5FBFD9874D08E6B241F8F`

`Test-SkyrealmImportPackage.ps1` 已逐项核对全部载荷哈希、版本、禁止文件、配置所有权、
双端菜单一致性和受管启动脚本。后台同源 `ModpackArchiveAnalyzer` 返回：

- `layout=Canonical`；
- `hasBlockingIssues=false`；
- Minecraft `1.21.1`、NeoForge `21.1.228`、Java `21`、最大玩家 `20`；
- 唯一服务端入口为 `server/start.bat`；
- 客户端 `4,457` 个文件，服务端 `347` 个文件；
- 拆出的客户端含菜单 JAR，服务端含菜单 JAR、经济插件、配置和受管启动脚本。

旧 `1.0.0` 和 `1.0.1` 候选包没有被覆盖。`1.0.1` 在双分析器验收中发现仍保留原始
`run.bat`，因此由 `1.0.2` 移除 `run.bat` / `run.sh` 并取代；不得把前两版用于部署。

## 6. 生产部署顺序

以下步骤尚未执行：

1. 备份生产 PostgreSQL，并在隔离数据库执行迁移 `029`、事务并发和恢复演练。
2. 为 Economy Service 配置令牌 SHA-256 与允许的 `skyrealm` 服务身份；明文令牌只通过
   运行时秘密注入，不进入包、Git、文档或聊天。
3. 发布 API，验证 `/healthz`、`/readyz`、未授权拒绝、错误服务 ID 拒绝和账本守恒。
4. 为长期生存服新建独立服控目标、目录、端口、内存和冲突组，不复用 `activity` 槽。
5. 在停服状态原子部署服务端与客户端 Test 通道，配置 Velocity modern forwarding、
   LuckPerms 共享和语音 UDP；部署结束仍保持停服。
6. 经明确授权后再启动精确交付包，依次完成 Arclight 类加载、Vault 所有权、命令冲突、
   交易补偿、Create/Sable/GriefPrevention 绕过与复制物品专项测试。
7. 按 `2/3/5/20` 人完成 TPS、MSPT、GC、内存、真人转账、断线重试和世界/数据库恢复验收。

## 7. 回滚

交易功能先故障关闭，再等待在途操作按原幂等键查询结果。服务端文件使用部署前快照原子
恢复；数据库迁移只前滚修复，不删除账本、审计、报价和幂等记录。移除 HechaoEconomy
后也不得自动恢复 Essentials 本地经济。所有回滚均保持 Velocity 二次授权和内部大厅
不可进入边界。

# 天域远征官方商城 0.1.0 候选记录

- 记录日期：`2026-08-24`
- 状态：本地候选，尚未部署 API、数据库、Minecraft 服务端或客户端档案
- 相关组件：API 迁移 `035_economy_server_shop`、HechaoEconomy `0.2.4`、Screen `0.2.10`
- 当前生产基线：API/插件/Screen 仍以线上实时核验结果为准，本候选不改变生产指针

## 功能

官方商城和服务器回收目录已分开：

- `/prices` 只读取服务器回收价，玩家放入物品后由 `/sell` 回收并发币；
- `/shop` 只读取配置了 `shop_unit_price` 的启用商品，玩家用金币购买；
- 购买在 PostgreSQL 单事务内锁定商品和余额，扣除玩家金币，并写入
  `system:shop-sink` 销毁分录；
- 物品先进入商城待领取记录，购买成功后插件自动打开 `/shop claim`，背包空间确认后才发放；
- 购买、领取均使用幂等键，超时重试不会重复扣款或复制物品；
- 商品、购买和领取按 `serverId` 隔离，不把一个服务端的物品交付到另一个独立槽；
- 购买价必须严格高于同商品回收价，API 在事务层拒绝低价套利配置。
- 后续提高回收价时也会锁定商品并拒绝越过商城售价的改价，数据库约束作为最后一道保护；
  管理员需要先调整或暂停商城售价。

## 管理配置

管理员手持待配置物品执行：

```text
/heco product set <回收价> [个人日限] [全服日限]
/heco product shop <商城购买价>
/heco product shop remove
```

商品 ID 同时支持原版和普通模组命名空间。商城价格不会从历史候选表自动导入，也没有在
本候选中擅自给 85 项商品定价；服主确认价格后才会出现在 `/shop`。未配置购买价时，玩家
会看到明确的“商城暂未上架商品”状态，而不是空白页面。

API 和数据库合同允许带命名空间及路径的物品 ID；当前 Bukkit 菜单和待领取流程仍要求
运行中的 Bukkit/Arclight `Material.matchMaterial` 能解析该 ID。未完成模组物品的运行时
解析适配前，不应把无法解析的模组物品配置为商城商品。

## API 合同

```text
GET  /v1/internal/economy/shop/products
POST /v1/internal/economy/shop/purchases
GET  /v1/internal/economy/shop/deliveries/{playerUuid}
POST /v1/internal/economy/shop/deliveries/claim
PUT  /v1/internal/economy/products/shop?itemId=<encoded-id>
POST /v1/internal/economy/products/shop/disable?itemId=<encoded-id>
```

管理接口保留旧的路径形式用于兼容，但插件默认使用查询参数形式，避免物品 ID 的路径部分
包含 `/` 时被路由层截断。

## 客户端交互

第三方 Screen 的快捷菜单包含“服务器商城”和“回收目录”。官方商城商品卡显示购买价，点击
后打开独立的商城购买确认页；玩家市场购买页仍使用原有标题。两者标题已拆分，避免客户端
把官方商城误识别为玩家市场。

## 构建制品

- HechaoEconomy `0.2.4`：`480903` 字节，SHA-256
  `E403DA7349D8AFE105D3B728743A30D92C235AC3C69EC346063E25A00ECEF28E`；
- Screen `0.2.10`：`990638` 字节，SHA-256
  `601A077D267CD6794B7D8DBF2C40975B08BF1A3E4094014B69D151A86A2345A6`。

## 验证与未完成门禁

- API 测试：`382` 通过、`1` 跳过、`0` 失败；跳过项是因为本机未提供隔离
  PostgreSQL 环境变量；
- HechaoEconomy：`39/39` 测试通过，`clean test build` 通过；
- NeoForge Screen：`113/113` 测试通过，`clean test build` 通过；
- .NET 整套解决方案：`829` 通过、`1` 跳过、`0` 失败；
- PostgreSQL 集成测试仍需在隔离数据库执行，当前本机没有将生产数据库作为测试目标；
- 需要两个真实测试账号完成购买、余额不足、重复请求、背包满、断线和服务端重启后的领取验收；
- 需要服主先确认首批商城价格，再制作新的客户端档案并只切换 Test；
- 本候选完成前不得推进 Gray/Production，也不得在没有维护窗口的情况下重启生产服务。

## 回滚

未部署则无需线上回滚。后续上线时必须保留迁移前数据库备份、旧插件 JAR、旧客户端档案
和不可变 OSS 对象；回滚程序版本不删除 `shop` 购买审计、待领取或余额分录。

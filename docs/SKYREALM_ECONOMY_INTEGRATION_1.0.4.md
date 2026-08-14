# 天域远征工业季经济集成 1.0.4

> 本版保留为历史验收记录。响应式屏幕候选已由 `1.0.5` 取代，见
> [`SKYREALM_ECONOMY_INTEGRATION_1.0.5.md`](SKYREALM_ECONOMY_INTEGRATION_1.0.5.md)。

> 状态：可由赫朝启动器后台标准导入的一键候选已完成；未上传后台、未部署 API/Agent、
> 未执行迁移、未注入令牌，未启动或重启任何 Minecraft 服务端。
> 日期：2026-08-14

## 本版变更

- HechaoEconomy 升级到 `0.1.2`：服主手持物品执行 `/heco product` 会得到可点击的常用
  价格、自定义价格/额度和暂停回收操作。
- `hechao.economy.admin` 仍是唯一商品管理权限，默认只授予 OP；每次商品变更继续把服主
  UUID、名称、前后值和时间写入 Economy Service 审计。
- 普通无自定义数据的模组物品可以由服主加入回收目录；命名、附魔、容器、带组件或其他
  元数据物品仍在插件侧拒绝。
- HechaoEconomyScreen 升级到 `0.1.1`，自定义屏幕新增“服主回收设置”入口。客户端只发送
  短期会话和固定 action ID，实际权限和商品写入仍由服务端裁决。
- API 升级到 `0.31.1`，增加支持完整命名空间和路径的查询式商品管理端点；旧端点保留兼容。
- 包内新增 `server/plugins/HechaoEconomy/服主快捷设置.txt`，包级校验器把命令、权限、
  模组物品支持和屏幕入口作为硬合同。

## 候选制品

| 项目 | 值 |
| --- | --- |
| 路径 | `E:\天域远征工业季-赫朝一键导入-1.0.4.zip` |
| 大小 | `1,586,002,019` 字节 |
| SHA-256 | `96007EFB9A0BCDA646DB8BA91DA5086222518F2BC4E4A3AF483A01A1DED0963A` |
| 载荷 | `4,799` 个文件，`1,584,236,697` 字节 |
| HechaoEconomy | `0.1.2`，`366,851` 字节，SHA-256 `D538110ECC3320F5F7DE7AAEC9FF872D56BAD9C228D6D8482BAF88BAF595D56E` |
| HechaoEconomyScreen | `0.1.1`，`23,434` 字节，SHA-256 `E0F9AE351D564D1ECC61586D09BB75DDC35C28F23855569186A1F4F768A2A2DD` |

`Test-SkyrealmImportPackage.ps1` 已逐项验证全部载荷哈希、禁止文件、外置令牌、受管启动
脚本、Essentials/TAB 所有权、新版 Bukkit 命令与管理员快捷设置 class、双端屏幕一致性、
服主说明和 NeoForge 精确版本合同，返回 `Valid`。

后台同源 Inspector 返回：

- `Canonical`，`hasBlockingIssues=false`，问题列表为空；
- Minecraft `1.21.1`、NeoForge `21.1.228`、Java `21`、最大玩家 `20`；
- 唯一服务端入口 `server/start.bat`；
- 客户端 `4,457` 个文件，SHA-256
  `A8898A37909C2D2263F45112DC67C9D3B75130697DB1F162F80CD338F6733A85`；
- 服务端 `348` 个文件，SHA-256
  `5B5A0FF5C21B594FE76C51C4C7A2C76A3E83D2A2FA607FEF0FB857A02C3EB282`。

完整 API `312/312`、完整 `.NET` 解决方案 `731/731`、HechaoEconomy `10/10`、
HechaoEconomyScreen `3/3` 通过；API Release 构建零警告零错误。自定义屏幕使用动态面板
宽度和三行紧凑布局，新增服主按钮不会在常见低高度 GUI 下裁出面板。

## 服主使用方式

1. 给服主授予 `hechao.economy.admin`。
2. 把要配置的原版或普通模组物品拿到主手。
3. 从自定义屏幕点击“服主回收设置”，或输入 `/heco product`。
4. 点击常用价格；需要精确额度时使用
   `/heco product set <单价> [个人日限] [全服日限]`。
5. 暂停回收使用 `/heco product remove`，玩家可用 `/shop` 查看当前启用目录。

无需修改 YAML、数据库或重新制作整合包。所有操作通过外置服务令牌访问权威 API，令牌
缺失、API 不可用、权限不足或物品带数据时均故障关闭。

## 生产门禁

1. 确认货币正式名称和精度；第一笔真实账本数据前完成。
2. 在隔离 PostgreSQL 应用迁移 `029`，完成账本守恒、并发、幂等和恢复演练。
3. 备份 API、数据库、owl5 Agent 配置、`E:\Survival2` 与世界。
4. 先发布 API `0.31.1`，再仅重启 Agent 到 `0.6.0`；不自动启动 Minecraft。
5. 后台上传本包，明确选择 `survival2` 并输入包含目标 ID 的确认文本；客户端先进入
   `Test` 通道，服务端部署后保持停止。
6. 外置注入经济令牌和 Velocity forwarding，使用普通模组材料及拒绝型带数据物品完成
   管理、报价、确认、审计和失败补偿验收。
7. 最后按 `2/3/5/20` 人完成 TPS、MSPT、GC、内存与交易并发灰度。

任何门禁失败都停止前滚并按既有 API release、Agent 安装器和受控目录备份回滚；不删除
账本、审计、导入记录或不可变 OSS 对象。

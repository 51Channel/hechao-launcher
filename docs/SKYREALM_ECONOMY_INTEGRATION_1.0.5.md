# 天域远征工业季经济集成 1.0.5

> 状态：可由赫朝启动器后台标准导入的一键候选已完成；未上传后台、未部署 API/Agent、
> 未执行迁移、未注入令牌，未启动或重启任何 Minecraft 服务端。
> 日期：2026-08-14

## 已实现能力

- HechaoEconomy `0.1.2` 提供权威余额、转账、报价出售、回收目录、Vault、PlaceholderAPI
  和服主商品管理。
- 服主手持物品执行 `/heco product` 会得到可点击常用价格、自定义价格/额度和暂停回收；
  `hechao.economy.admin` 是唯一管理权限，所有变更进入 Economy Service 审计。
- 普通无自定义数据的 Create 等模组物品可由服主加入回收目录；命名、附魔、容器、带
  组件或其他元数据物品继续在插件侧拒绝。
- HechaoEconomyScreen `0.1.2` 同时进入客户端和服务端，提供“服主回收设置”入口；使用
  动态宽度和三行紧凑布局，六个按钮在常见低高度 GUI 下不会裁出面板。
- 客户端只发送短期会话和固定 action ID，权限、物品检查、价格和写入均由服务端裁决。
- API `0.31.1` 支持完整小写命名空间和物品路径，旧商品端点保留兼容；Agent `0.6.0`
  支持显式授权的 `survival2` 受控部署目标，活动企划仍固定 `activity`。
- `survival2` 把 `plugins\HechaoEconomy\economy-token.txt` 与 `forwarding.secret` 一并列为
  主机受管文件；包内不能覆盖，现有目录与删除前快照都会保留，缺失时在目录切换前拒绝部署。
- 包内含 `server/plugins/HechaoEconomy/服主快捷设置.txt`，无需服主手改数据库或重新打包。

## 候选制品

| 项目 | 值 |
| --- | --- |
| 路径 | `E:\天域远征工业季-赫朝一键导入-1.0.5.zip` |
| 大小 | `1,586,002,351` 字节 |
| SHA-256 | `282227EAB5C0D82745108275A57A0D2DDE7DE9D298ED2FA887ABFF99E2956523` |
| 载荷 | `4,799` 个文件，`1,584,237,029` 字节 |
| HechaoEconomy | `0.1.2`，`366,851` 字节，SHA-256 `D538110ECC3320F5F7DE7AAEC9FF872D56BAD9C228D6D8482BAF88BAF595D56E` |
| HechaoEconomyScreen | `0.1.2`，`23,600` 字节，SHA-256 `B2E8913BD61EA3C8C36A41364E049A0699E51C59D170663661435688179A59C1` |

`Test-SkyrealmImportPackage.ps1` 已逐项验证全部载荷哈希、禁止文件、外置令牌、受管启动
脚本、Essentials/TAB 所有权、新版 Bukkit 命令与管理员快捷设置 class、双端屏幕一致性、
服主说明和 NeoForge 精确版本合同，返回 `Valid`。

后台同源 Inspector 返回：

- `Canonical`，`hasBlockingIssues=false`，问题列表为空；
- Minecraft `1.21.1`、NeoForge `21.1.228`、Java `21`、最大玩家 `20`；
- 唯一服务端入口 `server/start.bat`；
- 客户端 `4,457` 个文件，SHA-256
  `BE075EE938A3D21659A57C5126883909799FB6C342199BA32294196DFCE907D0`；
- 服务端 `348` 个文件，SHA-256
  `139FE4E8F3F0429D156FA9D27123E14B0FEEE7E56E5FF8EFF36B1BEDF5876281`。

完整 API `312/312`、完整 `.NET` 解决方案 `731/731`、HechaoEconomy `10/10`、
HechaoEconomyScreen `3/3` 通过；API Release 构建零警告零错误。

## 服主使用方式

1. 给服主授予 `hechao.economy.admin`。
2. 把要配置的原版或普通模组物品拿到主手。
3. 从自定义屏幕点击“服主回收设置”，或输入 `/heco product`。
4. 点击常用价格；需要精确额度时使用
   `/heco product set <单价> [个人日限] [全服日限]`。
5. 暂停回收使用 `/heco product remove`，玩家可用 `/shop` 查看当前启用目录。

所有操作通过外置服务令牌访问权威 API。令牌缺失、API 不可用、权限不足或物品带数据时
均故障关闭。

## 生产门禁

1. 确认货币正式名称和精度；第一笔真实账本数据前完成。
2. 在隔离 PostgreSQL 应用迁移 `029`，完成账本守恒、并发、幂等和恢复演练。
3. 备份 API、数据库、owl5 Agent 配置、`E:\Survival2` 与世界。
4. 将经济令牌外置到现有
   `E:\Survival2\plugins\HechaoEconomy\economy-token.txt`，确认 Velocity forwarding 也已
   就位；再发布 API `0.31.1`，仅重启 Agent 到 `0.6.0`，不自动启动 Minecraft。
5. 后台上传本包，明确选择 `survival2` 并输入包含目标 ID 的确认文本；客户端先进入
   `Test` 通道，服务端部署后保持停止。
6. 核对部署后的经济令牌和 Velocity forwarding 与部署前逐字节一致；使用普通模组材料及
   拒绝型带数据物品完成管理、报价、确认、审计和失败补偿验收。
7. 最后按 `2/3/5/20` 人完成 TPS、MSPT、GC、内存与交易并发灰度。

任何门禁失败都停止前滚并按既有 API release、Agent 安装器和受控目录备份回滚；不删除
账本、审计、导入记录或不可变 OSS 对象。

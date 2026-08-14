# 天域远征工业季经济插件设计

> 状态：源码与离线一键导入包已实现，尚未生产部署或运行精确交付包
> 审查日期：2026-08-14  
> 输入包：`E:\生存服 交接包（客户端服务端）.zip`  
> 输入包 SHA-256：`A0393BC880DE4E70181B244E8ED42774AEF582908E2F072D31552317931860E9`

交接包内置清单已完成全量流式复核：`4,798/4,798` 个载荷文件、
`1,589,543,694` 字节全部匹配，缺失、非法清单行和 SHA-256 不一致均为 `0`；
`manifest/payload.sha256` 自身哈希也与发布清单一致。

实现结果、制品哈希、导入验证和仍未验收的生产门槛见
[`SKYREALM_ECONOMY_INTEGRATION_1.0.3.md`](SKYREALM_ECONOMY_INTEGRATION_1.0.3.md)。

## 1. 已验证运行基线

交接包采用以下运行栈，而不是先前讨论中的 `1.20.1`：

- Minecraft `1.21.1`；
- Arclight NeoForge `1.0.2-SNAPSHOT-8086b06`；
- NeoForge `21.1.228`；
- Java `21`；
- 新世界 `world-skyrealm-industrial-season`，玩家和 OP 均为 `0`；
- 本机后端 `127.0.0.1:25580`，语音 UDP `24480`；
- JVM 参数为 `-Xms4G -Xmx10G`；
- 服务端达到过 `Done`，客户端、Create Aeronautics、Sable、语音和正常停服做过冒烟；
- 精确交付包启动、真实多人、玩法载具、OBS 和生产 Velocity 尚未验收。

交接包是 Arclight 混合核心，同时加载 NeoForge 模组和 Bukkit 插件。关键服务端模组包括
Create、Create Aeronautics、Sable、Lootr、Waystones、Simple Voice Chat 和 Spark。关键
Bukkit 插件包括 EssentialsX、GriefPrevention、LuckPerms、PlaceholderAPI、TAB、Vault
和自定义 `SkyrealmCore 0.1.0`。

## 2. 当前冲突与阻塞项

### 2.1 EssentialsX 经济冲突

EssentialsX 自带本地经济实现并可向 Vault 注册经济提供者。虽然交接包已禁用
`balance`、`balancetop`、`pay` 和 `sell` 命令，但 Essentials 经济服务本身仍然存在。
默认 `worth.yml` 还保留了过时示例价格，包含基岩、自动化物资和旧物品名称，不能作为
赫朝正式价格表。

新插件必须成为 Vault 的权威经济提供者，并在服务端完全加载后核验 Vault 当前选中的
提供者确实是 `HechaoEconomy`。如果发生提供者歧义，新交易必须故障关闭并产生明确告警，
不能静默写入 Essentials 本地余额。

### 2.2 LuckPerms 不是生产配置

包内 LuckPerms 使用本地 H2、`server=global`，目前没有玩家数据，也不会自动继承赫朝
平台的全局等级。正式接入前必须为长期生存服确定权限消费方案：使用既有共享
LuckPerms 存储与消息链路，或由平台提供专用只读权限桥。大厅 Tier Agent 仍只属于内部
大厅，不能复制进该后端。

### 2.3 SkyrealmCore 只有二进制

`SkyrealmCore.jar` 注册 `/settings`、TPA 和队伍命令，并使用本地 `settings.db` 保存设置
和队伍。当前赫朝仓库没有找到对应源码，交接清单中的源提交也不属于当前仓库。

经济系统不能继续塞入该二进制，也不能直接复用或修改 `settings.db`。首版建立独立
`HechaoEconomy` 插件；后续若要在 `/settings` 菜单中加入经济入口，必须先找回或重建
SkyrealmCore 源码，并通过显式 API 集成。

### 2.4 Arclight 混合核心风险

项目基线明确要求混合核心另立设计和风险审批。Create 载具、Sable、GriefPrevention、
Bukkit 事件和模组物品能力可能绕过彼此的保护或产生不完整物品视图。不能用一次
`Done` 冒烟替代真实玩法、复制物品和领地绕过测试。

### 2.5 生产接入仍未完成

- Arclight 的 Velocity forwarding 当前关闭且密钥为空；
- 后端 `online-mode=false` 只在完成代理转发、回环绑定和直连阻断后才可接受；
- 当前没有适配此精确 `1.21.1` 混合栈的赫朝指标组件验收；
- 语音 UDP 的公网地址、防火墙和鉴权仍需生产测试；
- 交接包不得直接覆盖现有生存服目录。

## 3. 推荐架构

经济系统拆为三个边界清楚的组件。

### 3.1 Hechao Economy Service

在 Launcher API 代码库内建立独立经济领域模块，复用现有 PostgreSQL、迁移、审计、
监控和发布流程。它是余额和交易流水的唯一权威来源。

职责：

- UUID 账户、可用余额和冻结余额；
- 双式账本与只追加冲正；
- 幂等交易、卖出报价、每日额度和全服材料预算；
- 玩家转账、系统商店、后续拍卖与收购订单；
- 管理后台审计、冻结、冲正和经济监控；
- 启动器和官网的只读余额及通知投影。

游戏服插件不直连 PostgreSQL。它通过带服务身份的内部 HTTPS API 访问经济服务；服务
凭据由生产主机外置配置和 ACL 管理，不进入 JAR、整合包、Git、日志或诊断包。

### 3.2 HechaoEconomy Bukkit 插件

首版直接运行于交接包的 Arclight Bukkit 层。

技术基线：

- Java `21`；
- Gradle；
- `plugin.yml` 的 `api-version` 为 `1.21`；
- 编译目标为 `1.21.1` Bukkit/Paper API，但业务代码只使用 Arclight 已验证的 Bukkit API；
- 不使用 NMS、CraftBukkit 内部类或未经验证的 Paper 专用序列化；
- 使用 Java 21 `HttpClient`，请求全部带超时、取消和幂等键；
- 可选依赖 PlaceholderAPI、LuckPerms，强依赖 Vault；
- 在 Vault 中以最高优先级注册赫朝经济提供者，并在 `ServerLoadEvent` 后自检所有权。

职责：

- `/money`、`/balance` 和余额查询；
- `/pay` 与大额确认；
- `/sell` 的报价、确认、物品移除和失败补偿；
- `/shop` 的箱子 GUI 与原子购买；
- Vault Economy 兼容；
- PlaceholderAPI 变量，例如 `%hechao_balance%`；
- TAB 侧边栏所需的只读数据；
- 本地短时只读缓存、断路器、管理员健康命令和脱敏诊断。

插件不可把远端不可用降级为本地余额。经济 API 不可用时允许只读缓存展示，但新交易
统一暂停。

### 3.3 HechaoEconomy NeoForge Bridge

该组件不属于首版阻塞项。只有在开放模组物品拍卖、模组物品回收或全屏客户端界面时
再开发，并同时安装到对应客户端和服务端。

职责：

- 使用 NeoForge 原生注册表读取模组物品 ID；
- 保存并恢复完整 `ItemStack`、NBT、数据组件和模组能力；
- 拒绝未知模组、版本不一致和已删除注册项；
- 为赫朝整合包提供可选的全屏经济界面；
- 通过受限内部接口与 Bukkit 插件或 Economy Service 协作。

在 Bridge 上线前，拍卖和 `/sell` 只接受经过白名单验证的原版物品。Create、Sable、
容器、载具、附魔物品和未知模组物品默认拒绝。

## 4. 命令与能力所有权

| 能力或命令 | 唯一所有者 | 当前冲突 | 处理方式 |
| --- | --- | --- | --- |
| `/money`、`/balance` | HechaoEconomy | EssentialsX | 保持 Essentials 命令禁用，由赫朝插件注册 |
| `/pay` | HechaoEconomy | EssentialsX | 保持 Essentials `/pay` 禁用，不读取 Essentials 余额 |
| `/sell` | HechaoEconomy | EssentialsX `worth.yml` | 保持 Essentials `/sell` 禁用，赫朝使用 API 商品目录 |
| `/shop` | HechaoEconomy | 无 | 首版箱子 GUI |
| `/heco admin` | HechaoEconomy | Essentials `/eco` | 不复用 `/eco`，管理员操作必须带审计原因 |
| `/settings`、TPA、队伍 | SkyrealmCore | 无 | 保持现状，不写入经济数据库 |
| 余额占位符 | HechaoEconomy | Essentials/Vault 占位符 | TAB 只使用 `%hechao_balance%` |
| 领地购买 | GriefPrevention | Vault 提供者 | 后续只允许通过 HechaoEconomy 扣款 |

Essentials 的 `min-money=-10000`、`max-money`、`starting-balance` 和 `worth.yml` 对赫朝账本
均不生效。正式配置应继续禁用 Essentials 经济命令，并移除或隔离默认价格表，防止管理员
误启用。

## 5. 首版交易流程

### 5.1 查询余额

1. 插件以玩家正版 UUID 查询 Economy Service；
2. 成功时刷新短时只读缓存；
3. 失败时可显示带时间戳的缓存，但明确标记暂不可交易。

### 5.2 玩家转账

1. 插件本地校验权限、金额、接收者和频率；
2. Economy Service 在单个数据库事务中完成扣款、入账和审计；
3. 重试沿用相同幂等键；
4. 超时后查询原交易状态，不重新生成交易；
5. 大额转账要求玩家二次确认。

### 5.3 系统回收

1. 插件从主手或指定背包槽构造只读报价请求；
2. API 返回商品、数量、基准价、个人衰减、全服衰减、额度余额和报价有效期；
3. 玩家确认后，插件重新核验完全相同的物品快照；
4. 插件移除物品并提交交易；
5. 提交成功后到账；发生不确定结果时按操作 ID 查询；明确失败时将物品放入受控补偿邮箱，
   不直接掉落到世界。

首版不提供“一键出售全部未知物品”，也不读取 Essentials `worth.yml`。

## 6. 建议源码结构

```text
src/
  Hechao.EconomyPlugin/
    build.gradle
    settings.gradle
    src/main/java/world/hechao/economy/
      HechaoEconomyPlugin.java
      api/
      commands/
      economy/
      gui/
      inventory/
      placeholder/
      vault/
    src/main/resources/
      plugin.yml
      config.yml
    src/test/java/
  Hechao.EconomyBridge.NeoForge/
    ... later ...
```

Economy Service 的数据库模型和管理接口继续放在现有 Launcher API 项目中，不在 Java
插件内复制一份业务规则。

## 7. 分阶段开发

### 阶段 A：基础账本和只读接入

- PostgreSQL 迁移、双式账本、幂等事务和审计；
- 插件健康检查、`/money`、Vault 提供者和 PlaceholderAPI；
- Essentials 经济冲突自检；
- API 故障关闭和脱敏诊断。

### 阶段 B：转账、回收和商店

- `/pay`、额度、确认和风控；
- 原版物品白名单 `/sell`；
- 箱子式 `/shop`；
- 补偿邮箱；
- TAB 余额和每日铸币/回收监控。

### 阶段 C：玩家市场

- 固定价拍卖、挂单费和成交税；
- 标准物品收购订单；
- 冻结余额、离线交付和价格历史；
- 只允许精确兼容组跨服领取物品。

### 阶段 D：模组物品与客户端界面

- NeoForge Bridge；
- 模组物品序列化和兼容版本锁；
- 客户端全屏经济菜单；
- 经过产量审计后逐项开放模组物品回收。

## 8. 测试与生产门槛

自动测试至少覆盖：

- 账本分录和为零、余额不能超发、冲正只追加；
- 所有写接口的幂等重试；
- 并发转账和并发购买；
- 报价过期、物品变化、背包已满和断线补偿；
- Vault 提供者所有权和 Essentials 冲突；
- API 超时、无效响应、服务重启和数据库回滚；
- 未知模组物品、容器、命名物和附魔物默认拒绝。

生产前必须在精确交付包上完成：

1. Arclight 精确版本启动和正常停止；
2. Velocity modern forwarding、正确 UUID、错误授权拒绝和后端直连阻断；
3. LuckPerms 全局等级读取且不会写坏大厅权威状态；
4. Essentials、Vault、TAB、GriefPrevention 与 HechaoEconomy 的唯一能力所有者检查；
5. Create Aeronautics、Sable、领地保护和物品复制专项测试；
6. 世界备份、数据库备份、恢复演练和插件移除回滚；
7. `2/3/5/20` 人分阶段 TPS、MSPT、GC、内存和交易并发验收。

测试服和生产服均默认以停止状态完成部署。未经当前任务明确授权，不启动、重启或切换
任何生产 Minecraft 后端。

## 9. 回滚边界

- 插件通过功能开关先暂停新交易，再等待在途操作结算；
- Economy Service 保留只读余额和交易查询；
- 数据库迁移只允许前滚修复，不删除账本、审计或已使用幂等键；
- 移除插件后不得自动恢复 Essentials 本地经济；
- 模组物品尚有托管或邮箱投递时，不允许移除对应模组或降低整合包版本；
- 回滚不得关闭 Velocity 授权、开放后端直连或把玩家送入内部大厅。

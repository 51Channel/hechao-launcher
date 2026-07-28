# 生产剩余验收门槛

> 只读快照时间：2026-07-28 01:40（Asia/Shanghai）
>
> RAM v5 增量验收更新时间：2026-07-28 03:49（Asia/Shanghai）
>
> 游戏服、客户端兼容与世界备份更新时间：2026-07-28 07:40（Asia/Shanghai）
>
> 恐怖整蛊核心 modern forwarding 复测更新时间：2026-07-28 09:48（Asia/Shanghai）
>
> 恐怖整蛊运行与正式备份验收更新时间：2026-07-28 15:20（Asia/Shanghai）
>
> 本文记录生产门槛的当前状态，不把自动测试、静态部署或合成账号扩大解释为真实验收。
>
> 命名边界：下文历史写法 `PVP`、目标 `pvp` 和档案
> `pvp-fabric-1.20.1` 实际都指 owl9 的恐怖整蛊 Fabric 服
> `C:\mc\server`，不指 `E:\MinecraftServer` 的真正 PVP Purpur 服。后者未纳入
> 本轮赫朝启动器验收。见
> [`OWL9_DUAL_BACKEND_OPERATIONS.md`](OWL9_DUAL_BACKEND_OPERATIONS.md)。

## 1. 已确认基线

- 客户端兼容保护源码提交为
  `c2b50e2ac75b8bc9a66cfcb9691c7ee566ebfd57`，世界恢复验证器提交为
  `b0c810e0095a30ba686108994c6cf7a1db0d8628`。
- API `0.20.2` 的 `healthz` 为 `ok`，`readyz` 为 `ready`，数据库迁移为
  `17/17`。
- 六份生产档案都已绑定 production 通道，没有暂停版本。
- 五个 Velocity 目标均已启动并产生 TPS/MSPT/GC；20 至 30 人负载仍未验收。
- Velocity 授权插件 `0.3.0` 以 `monitor` 运行；客户端版本和模组档案不兼容会立即
  拒绝，其他权限拒绝仍只记录。
- 真实诊断上传 `1` 份、失败 `0`，管理员下载审计 `1` 条；本轮管理员下载
  ZIP 为 `707` 字节，SHA-256 为
  `1C53C309DDA3D1D9A905836E79A041EDCD4DDD03C543E0424119C876AAA6BF92`，
  只包含 `diagnostic.json` 和 `README.txt`。内存扫描未发现令牌、密码/密钥、
  邮箱、公网 IPv4 或用户绝对路径。
- 论坛统一账号共 `22` 个，均有论坛身份、邮箱和密码；待处理论坛会话撤销为 `0`。
- LuckPerms 快照 `115` 条且全部新鲜，四个目标组都有样本。

## 2. 历史告警说明

01:40 快照中共有 `5` 条活跃告警：

| 严重度 | 数量 | 原因 |
| --- | ---: | --- |
| Critical | 2 | 活动服与恐怖整蛊当前关闭，但目录仍配置为 Online |
| Warning | 3 | 大厅、Survival1、Survival2 尚未加载 Paper/Purpur 指标代理 |

这些是重启前的历史快照，不再代表 07:40 的五服运行状态。活动窗口关闭时，仍应由
管理员把对应目录切为 Maintenance 或 Closed，避免把计划内停服长期显示成
Critical。当前告警应以管理后台实时页和新的 API 聚合查询为准，不能继续引用旧计数。

## 3. 门槛状态

### 3.1 RAM v5 与平台数据异地备份：已完成

RAM v5 策略文件 SHA-256 为
`24D26EBE01688FC6B508D7EB22ACE95975BB82636246EE150633678171417A9A`，
控制台回读确认默认版本为 v5。它只包含 `GetObject/PutObject` 和五个批准前缀，
不包含 List、Delete、ACL、版本管理或 Bucket 级权限。

明确确认后已完成两轮真实加密上传和立即回读。第一轮在 owl5 解密后回到 API 主机
隔离验证论坛 SQLite 与 Sub2API `77` 张表，临时数据库自动删除；第二轮真实成功备份
用于清除受控失败标记。平台监控器 `0.1.2` 分别记录 Critical/Active 和
Resolved，触发与恢复邮件均成功投递。offsite timer 现为 `enabled/active`，API 与
论坛未重启，所有游戏服进程保持原状。

非秘密证据见
[`evidence/PLATFORM_DATA_BACKUP_RAM_V5_ACCEPTANCE_2026-07-28.json`](evidence/PLATFORM_DATA_BACKUP_RAM_V5_ACCEPTANCE_2026-07-28.json)。

### 3.2 管理后台真实页面

数据库已有 `1` 份 MFA 凭据，但当前有效管理员会话为 `0`。只读浏览器检查正常落到
“需要管理员身份”页面，没有尝试绕过登录。

管理员需要从启动器重新打开管理后台并完成 MFA。随后逐页核对运行数据、服务状态、
告警、档案、玩家和审计页面。任何暂停、回滚、账号停用、权限修改或告警确认都属于
写操作，必须使用专门测试对象和明确回滚步骤，不能为验收而修改真实玩家。

### 3.3 大厅、生存服、活动服与恐怖整蛊指标：已完成单人基线

受控重启/启动窗口已经完成，代理均已加载：

| 目标 | TPS | MSPT | GC 累计暂停 |
| --- | ---: | ---: | ---: |
| Survival1 | `19.996649` | `1.1225 ms` | `741 ms` |
| Survival2 | `20.000241` | `1.0375 ms` | `512 ms` |
| Lobby | `20.003904` | `1.8530 ms` | `394 ms` |
| Activity | `20` | `5.7745 ms` | `253 ms` |
| 恐怖整蛊（legacy `pvp`） | `20` | `12.7157 ms` | `1,413 ms` |

这些是无真实负载的启动基线，不替代活动日容量测试。大厅 LuckPerms 代理也已加载，
仍需使用专门测试账号完成四级改回。

15:20 再次对恐怖整蛊做 15 秒只读采样：PID 和 `25565` 监听均为 `7216`，
`20/20/20 TPS`、`12.591603775 MSPT`，GC 增量为 `0 ms`；真正 PVP 进程数为
`0`。这仍是空载健康样本，不替代多人测试。

三份 Paper 世界正式归档、SHA、完整解压和隔离恢复已验收；当前远端三份状态均指向
存在的 ZIP，`.partial`、`active.json`、孤立旁车和专属 VSS 均为 `0`。owl9
恐怖整蛊也已生成 `4,149,156,327` 字节正式归档，PID 未变化；异机副本完成
`2,493/2,493` 文件哈希比对和 `2,370/2,370` 区域全量检查。源世界既有的
`22` 个零字节 `entities`/`poi` 占位文件与恢复副本精确一致，地形空文件为 `0`；
验证器默认仍严格拒绝，只有完整匹配源端清单时才接受。真正 PVP 未触碰。详见
[`evidence/OWL9_HORRORPRANK_RUNTIME_AND_WORLD_BACKUP_2026-07-28.json`](evidence/OWL9_HORRORPRANK_RUNTIME_AND_WORLD_BACKUP_2026-07-28.json)。

### 3.4 恐怖整蛊真实 Velocity 路由：代理单层隔离五轮已通过

正确赫朝客户端已完成恐怖整蛊 Fabric `1.0.0` 档案、独立 Java 17 和全部清单文件安装。
第一次真实启动已由 Velocity 通过历史目标 `pvp` 正确路由到恐怖整蛊服，但后端自定义包解码失败并立即断开。
在 `save-all flush`、优雅停服和独立备份后，owl9 已安装官方 CrossStitch `0.1.6`；
修复后的 `HorrorPrank` 持久任务同时加载 FabricProxy-Lite `2.6.0` 与 CrossStitch，
并在 SSH 会话结束后继续监听。真实正版会话由 Velocity 从初始 `lobby` 定向到 `pvp`，
后端稳定保持 `586` 秒且没有新的无效包或解码错误；语音认证、后端缓存、最新玩家数据
与启动身份的 UUID 内存哈希一致。公网直连探针收到 `velocity:player_info` 后被明确
拒绝为必须通过 Velocity。客户端正常关闭，启动器记录退出码 `0`。

随后已补做多次成功重连。因此核心 modern forwarding、身份与直连拒绝已经通过。
恐怖整蛊服为 `1.20.1`、大厅为 `1.21.11`，跨版本 `/hub` 还必须区分翻译层。
真实正版前两次 `/hub` 已通过命令、授权和 Lobby
登录链，但分别在 Velocity `3.4.0-SNAPSHOT` 与 `3.5.1` 因
`accept_teleportation` 多余 `131` / `16` 字节断开，不能记为成功。回环隔离核心现为
Velocity `4.0.0` build `6` 和独立 Java 25；核对后发现代理与大厅曾重复加载相同
ViaVersion/ViaBackwards。改为代理 Via `0`、大厅 Via `2` 的后端单层翻译后，
首条回程曾成功，但第二次 `/hub` 又出现多余 `17` 字节，判定为间歇性失败。
协议 `393`、`763` 与 `774` status 协商、受限凭据探针、管理脚本防回归断言及零错误
启动均通过。API `0.21.0` 也已在生产数据库副本上通过迁移默认关闭、只开启大厅后的
恐怖整蛊回程授权、反向隔离和模组档案防绕过验收。候选没有部署，生产数据库仍为迁移
17。隔离 Authorizer `0.3.1` 已修复成功转服后会话来源不更新的问题并通过
`20/20` 测试，生产仍为 `0.3.0`。后端单层翻译候选的首条真实
恐怖整蛊 -> `/hub` -> Lobby 已通过并稳定超过 `591` 秒；四段日志解码错误为 `0`，
两个后端的名称与 UUID 内存比对一致，聊天、LuckPerms、TPS/MSPT 与短窗口 GC 均
通过。大厅反向请求 `pvp` 已被 Authorizer 以客户端不兼容正确拒绝，目标后端连接数
为 `0`，玩家仍在线；首次会话随后以启动器退出码 `0` 正常结束。第二枚 fresh grant
已由真实客户端消费并再次进入恐怖整蛊，稳定超过 `410` 秒；`20 TPS`、约
`14.64 MSPT`、短窗口 GC 增量 `0 ms`，客户端、代理和 API 错误均为 `0`。

最终隔离环境改为代理 Via `2`、独立 Lobby Via `0`。真实正版客户端连续五轮完成
fresh grant、恐怖整蛊登录、`/hub`、隔离大厅登录和正常退出；代理、两个后端、
客户端及候选 API 的协议/解码错误均为 `0`。因此生产迁移固定使用代理单层翻译，
仍须完成生产 API 发布、可回滚代理迁移和生产真实回程。
证据见
[`PVP_RETURN_PROTOCOL_STAGING_2026-07-28.json`](evidence/PVP_RETURN_PROTOCOL_STAGING_2026-07-28.json)
与
[`API_PROTOCOL_TRANSLATION_CANDIDATE_2026-07-28.json`](evidence/API_PROTOCOL_TRANSLATION_CANDIDATE_2026-07-28.json)。
实机会话与当前单层翻译状态见
[`PVP_RETURN_REAL_SESSION_2026-07-28.json`](evidence/PVP_RETURN_REAL_SESSION_2026-07-28.json)。
完成这些门槛前不得切换 Velocity `enforce`。

### 3.5 四级真实账号

当前社区账号 `22` 个，但只有 `1` 个绑定 Minecraft，且平台等级分布为：

| 等级 | 数量 |
| --- | ---: |
| Member | 21 |
| Participant | 0 |
| Collaborator | 0 |
| Administrator | 1 |

因此尚不具备普通、VIP、管理员和服主四级真实灰度条件。必须使用真实正版账号完成绑定、
目录过滤、允许/拒绝、目标服转移和拒绝路径，不能用数据库夹具替代。

### 3.6 遥测与人数灰度

正确赫朝客户端已完成管理员正版登录、基础档案进入 Lobby、Survival1、Survival2
和返回大厅。Activity `1.0.10` 与恐怖整蛊 `1.0.0` 也已完成生产安装：两者合计 `8,503`
个清单文件无缺失，只有两个客户端运行时可变配置与发布对象不同。Activity 已完成
单账号真实路由和正常退出；恐怖整蛊已完成修复后的核心进服、身份、直连拒绝与正常退出，
重连、两档案修复/回滚和多人样本仍需按以下
顺序补齐；真正 PVP Purpur 服不在这份列表中：

1. 管理员本机完整安装、修复、回滚、进服和退出；
2. 2 至 3 人内部灰度；
3. 5 人灰度；
4. 20 人灰度；
5. 活动日 20 至 30 人 TPS/MSPT/GC、网络与下载容量测试。

每一级只有在上一阶段无阻断问题且诊断、告警和回滚证据完整后才能扩大。

## 4. 最终强制顺序

1. [核心已完成] 恐怖整蛊服持久启动、统一入口、身份转发、直连拒绝和正常退出已通过；继续补成功重连、皮肤/权限与专用回程。
2. 真实四级账号与全部转服路径在 Velocity `monitor` 下通过。
3. 安排代理维护窗口，手动切换并重启到 `enforce`。
4. 验证无授权、低等级、维护服、未知目标、过期授权和 API 故障均按设计拒绝。
5. 稳定后再启用 `Authentication__EnforceCatalogAuthentication=true`。
6. 完成 5 人和 20 人灰度及一次真实回滚。

机器可读快照见
[`evidence/PRODUCTION_ACCEPTANCE_GATE_AUDIT_2026-07-28.json`](evidence/PRODUCTION_ACCEPTANCE_GATE_AUDIT_2026-07-28.json)。

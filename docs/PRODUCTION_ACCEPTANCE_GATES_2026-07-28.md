# 生产剩余验收门槛

> 只读快照时间：2026-07-28 01:40（Asia/Shanghai）
>
> 本文只记录尚缺的生产证据，不把自动测试、静态部署或合成账号扩大解释为真实验收。

## 1. 已确认基线

- GitHub `main` 与本机一致，提交为
  `654b3f038ae7a7727965a3535d36d4fb65de3a74`。
- API `0.20.1` 的 `healthz` 为 `ok`，`readyz` 为 `ready`，数据库迁移为
  `17/17`。
- 六份生产档案都已绑定 production 通道，没有暂停版本。
- 五个 Velocity 目标都有新鲜心跳；大厅、Survival1、Survival2 在线，活动服和
  PVP 关闭。
- 真实诊断上传 `1` 份、失败 `0`，管理员下载审计 `1` 条；本轮管理员下载
  ZIP 为 `707` 字节，SHA-256 为
  `1C53C309DDA3D1D9A905836E79A041EDCD4DDD03C543E0424119C876AAA6BF92`，
  只包含 `diagnostic.json` 和 `README.txt`。内存扫描未发现令牌、密码/密钥、
  邮箱、公网 IPv4 或用户绝对路径。
- 论坛统一账号共 `22` 个，均有论坛身份、邮箱和密码；待处理论坛会话撤销为 `0`。
- LuckPerms 快照 `115` 条且全部新鲜，四个目标组都有样本。

## 2. 当前不是故障的告警

生产中共有 `5` 条活跃告警：

| 严重度 | 数量 | 原因 |
| --- | ---: | --- |
| Critical | 2 | 活动服与 PVP 当前关闭，但目录仍配置为 Online |
| Warning | 3 | 大厅、Survival1、Survival2 尚未加载 Paper/Purpur 指标代理 |

这些告警与当前运行状态一致。活动窗口关闭时，应由管理员把对应目录切为
Maintenance 或 Closed，避免把计划内停服长期显示成 Critical。这里没有自动修改目录
状态，也没有代替管理员确认告警。

## 3. 必须按顺序完成的门槛

### 3.1 RAM v5 与平台数据异地备份

当前 RAM v5 策略文件 SHA-256 为
`24D26EBE01688FC6B508D7EB22ACE95975BB82636246EE150633678171417A9A`，
只增加 `backups/services/*` 和 `backups/recovery/*` 等既定前缀的
`GetObject/PutObject`，不包含 List、Delete、ACL 或 Bucket 级权限。

当前 offsite service 与 timer 都是 inactive，timer 为 disabled，暂存和恢复目录均
为空。只有收到精确确认 `确认创建并启用 RAM v5` 后，才允许：

1. 创建并启用 RAM v5；
2. 执行真实加密 OSS 上传和立即回读；
3. 在异地主机完成隔离恢复；
4. 启用 timer；
5. 验证失败与恢复告警。

### 3.2 管理后台真实页面

数据库已有 `1` 份 MFA 凭据，但当前有效管理员会话为 `0`。只读浏览器检查正常落到
“需要管理员身份”页面，没有尝试绕过登录。

管理员需要从启动器重新打开管理后台并完成 MFA。随后逐页核对运行数据、服务状态、
告警、档案、玩家和审计页面。任何暂停、回滚、账号停用、权限修改或告警确认都属于
写操作，必须使用专门测试对象和明确回滚步骤，不能为验收而修改真实玩家。

### 3.3 大厅和生存服手动重启窗口

三个进程仍创建于 2026-07-26 之前，因此磁盘上的新代理和备份计划尚未被当前实例
加载。当前状态是：

- `HechaoServerMetrics/metrics.json`：三服均不存在；
- 大厅等级代理启用日志：不存在；
- 正式世界 ZIP：`0`；
- `.partial`、`active.json`、状态文件和 VSS 残留：均为 `0`。

这证明目前没有新失败，只是还没经过服主控制的重启窗口。服主手动重启后，需要等待
错峰计划并验证 TPS/MSPT/GC、等级改回测试、正式 ZIP、SHA-256、条目数、剩余空间和
隔离恢复。运维自动化不得代为启动或重启。

### 3.4 PVP 真实 Velocity 路由

FabricProxy-Lite `2.6.0` 与 modern forwarding 密钥已经静态部署，PVP 仍关闭。
服主手动开服后必须验证统一入口、UUID/名称/皮肤/权限、直连拒绝、`/hub` 和断线重连。
完成前不得把 PVP 路由记为生产通过，也不得切换 Velocity `enforce`。

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

生产遥测当前只有 `1` 条 `LauncherStarted/Success`。安装、修复、回滚、Launch 和
GameExit 成功样本都是 `0`。应按以下顺序补齐：

1. 管理员本机完整安装、修复、回滚、进服和退出；
2. 2 至 3 人内部灰度；
3. 5 人灰度；
4. 20 人灰度；
5. 活动日 20 至 30 人 TPS/MSPT/GC、网络与下载容量测试。

每一级只有在上一阶段无阻断问题且诊断、告警和回滚证据完整后才能扩大。

## 4. 最终强制顺序

1. 真实四级账号与全部转服路径先在 Velocity `monitor` 下通过。
2. 安排代理维护窗口，手动切换并重启到 `enforce`。
3. 验证无授权、低等级、维护服、未知目标、过期授权和 API 故障均按设计拒绝。
4. 稳定后再启用 `Authentication__EnforceCatalogAuthentication=true`。
5. 完成 5 人和 20 人灰度及一次真实回滚。

机器可读快照见
[`evidence/PRODUCTION_ACCEPTANCE_GATE_AUDIT_2026-07-28.json`](evidence/PRODUCTION_ACCEPTANCE_GATE_AUDIT_2026-07-28.json)。

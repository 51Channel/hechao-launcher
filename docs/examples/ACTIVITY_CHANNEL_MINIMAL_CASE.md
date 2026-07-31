# 轻量案例：20 人集合签到

> 用途：让接手 Codex 理解如何在现有赫朝框架中同时开发客户端和服务端活动功能。
>
> 状态：纯示例，不是待执行生产发布；文中的版本、目录和 ID 未在生产注册。

## 1. 目标

制作一个名为“集合签到”的 NeoForge `1.21.11` 小活动：

- 玩家进入后按一个客户端按钮或执行 `/ready` 表示准备；
- 服务端验证玩家身份、回合、游戏模式和请求频率，再记录准备状态；
- 服务端向全体同步 `已准备人数/在线参与人数`；
- 20 人全部准备后只广播“可以开始”，不自动改世界、不传送、不生成实体；
- 离线、死亡进入观察者或退出当前回合时自动取消准备；
- 客户端只显示结果，不能直接把自己写成已准备。

它刻意不包含地图生成、复杂 AI 或大量资源，方便验证活动框架本身。

## 2. 活动描述

对应机器可读样例：
[`activity-spec.example.json`](activity-channel/activity-spec.example.json)。

| 字段 | 示例值 |
| --- | --- |
| `activityId` | `ready-check` |
| `serverId` / `controlTargetId` | `activity-ready-check` |
| `profileId` | `activity-ready-check-neoforge-1.21.11` |
| Minecraft / NeoForge / Java | `1.21.11` / `21.11.42` / `21` |
| Velocity 目标 | 固定 `activity` |
| 后端 | owl5 `127.0.0.1:25568` |
| 冲突组 | `owl5-activity-slot` |
| 示例目录 | `E:\Activities\ready-check` |
| 最低等级 | `Participant` |
| 人数 | `1-20` |

这是独立玩法，所以使用独立 `profileId`。若只是在现有画画躲猫猫中修一个兼容缺陷，
应沿用 `activity-neoforge-1.21.11` 并提高档案版本，而不是照此创建新档案。

## 3. 源码布局

```text
ready-check/
  AGENTS.md
  README.md
  build.gradle
  gradle.properties
  settings.gradle
  src/main/java/world/hechao/readycheck/common/Protocol.java
  src/main/java/world/hechao/readycheck/common/ReadyIntentPayload.java
  src/main/java/world/hechao/readycheck/common/ReadyStatePayload.java
  src/main/java/world/hechao/readycheck/server/ReadyService.java
  src/main/java/world/hechao/readycheck/server/ReadyCommand.java
  src/main/java/world/hechao/readycheck/client/ReadyHud.java
  src/main/java/world/hechao/readycheck/client/ReadyKeyBinding.java
  src/main/resources/META-INF/neoforge.mods.toml
  src/main/resources/assets/hechao_ready_check/lang/zh_cn.json
  src/test/java/world/hechao/readycheck/server/ReadyServiceTest.java
```

使用目标 NeoForge MDK 的真实 API 和映射生成工程，不从旧 JAR 猜接口。下面代码是职责
示意，不是可直接复制的 NeoForge API：

```java
public ReadyResult setReady(PlayerSnapshot player, boolean requested, long nowTick) {
    if (!round.isCollectingReady()) return ReadyResult.wrongPhase();
    if (!player.isParticipant()) return ReadyResult.notParticipant();
    if (!player.isAlive() || player.isSpectator()) return ReadyResult.notAlive();
    if (!rateLimiter.tryAcquire(player.uuid(), nowTick)) return ReadyResult.rateLimited();

    boolean changed = requested
        ? readyPlayers.add(player.uuid())
        : readyPlayers.remove(player.uuid());
    return ReadyResult.accepted(changed, readyPlayers.size(), round.participantCount());
}
```

## 4. 网络协议

协议常量：

```text
modId: hechao_ready_check
protocolVersion: 1
C2S: ready_intent_v1 { requested: bool, clientSequence: uint32 }
S2C: ready_state_v1 { revision: uint64, ready: int32, total: int32, selfReady: bool }
```

规则：

1. 两个载荷都声明最大字节数，解码前检查长度。
2. C2S 只表达“请求准备/取消”，不携带服务端计数、角色或胜负。
3. 每名玩家最多每秒接受 4 个意图；重复 `clientSequence` 不产生第二次状态变化。
4. 网络线程只完成解码和基础长度检查，`ReadyService` 在服务端线程执行。
5. 每次有效变化增加 `revision`；客户端丢弃较旧 S2C 状态。
6. 客户端协议不匹配时给出“活动客户端版本不匹配”，不让玩家带错误状态进入。

## 5. 服务端行为

- 玩家加入当前回合：默认未准备，服务端下发完整状态。
- 玩家死亡、变为观察者、退出、换维度离开活动世界：服务端移除准备状态。
- `/ready` 与客户端按钮调用同一个 `ReadyService`，避免两套规则漂移。
- 20 人全部准备只广播一次；之后重复包不会重复广播。
- 回合 ID 改变时原子清空集合、限流器和序号缓存。
- 服务端日志记录回合 ID、玩家 UUID 的不可逆短摘要、结果码和计数，不记录令牌、IP 或
  聊天内容。

客户端行为：

- HUD 显示服务端下发的计数；断线或切服立即清空本地缓存。
- 点击后可短暂显示“正在确认”，但收到服务端状态前不能显示最终成功。
- 按钮冷却只是减少误触；修改客户端绕过冷却仍会被服务端限流。

## 6. 自动测试

`ReadyServiceTest` 至少包含：

| 用例 | 预期 |
| --- | --- |
| 正常玩家准备 | 集合增加 1，revision 增加 |
| 同一序号重复 | 计数不变 |
| 观察者准备 | 拒绝，计数不变 |
| 死亡玩家准备 | 拒绝，计数不变 |
| 错误回合阶段 | 拒绝 |
| 每秒第 5 次请求 | 限流 |
| 玩家退出 | 自动移除 |
| `7` 人全部准备 | 精确广播一次 |
| `20` 人全部准备 | 精确广播一次 |
| 新回合 | 所有旧状态清空 |
| 旧 S2C revision | 客户端忽略 |

构建与专用服务端冒烟：

```powershell
pwsh -NoLogo -NoProfile -Command '& .\gradlew.bat clean test build'
pwsh -NoLogo -NoProfile -Command '& .\gradlew.bat runServer'
```

专用服务端日志必须显示正确 mod ID 和协议版本，且没有客户端类加载异常。随后用两个本地
客户端验证按钮、命令、重复点击、死亡观察者和重连。

## 7. 客户端档案

干净源应只增加同一次构建的：

```text
mods/hechao-ready-check-0.1.0.jar
config/hechao-ready-check-client.toml
hechao-profile.json
```

服务端放置同一 JAR，并比较 SHA-256：

```powershell
$ClientJar = '<clean-source>\mods\hechao-ready-check-0.1.0.jar'
$ServerJar = '<server-staging>\mods\hechao-ready-check-0.1.0.jar'

$ClientHash = (Get-FileHash -LiteralPath $ClientJar -Algorithm SHA256).Hash
$ServerHash = (Get-FileHash -LiteralPath $ServerJar -Algorithm SHA256).Hash
if ($ClientHash -ne $ServerHash) { throw 'Client/server activity JAR mismatch.' }
```

`hechao-profile.json`：

```json
{
  "schemaVersion": 1,
  "versionId": "1.21.11-NeoForge_21.11.42",
  "javaMajorVersion": 21
}
```

然后按总规范执行发布器 `publish`、`verify` 和 `validate-release`。假设档案初版为
`1.0.0`，正式标签为：

```text
profile-activity-ready-check-neoforge-1.21.11-v1.0.0
```

## 8. 基础组件计划与服务端控制目标

在创建服务端目录前，先从
[`component-plan.example.json`](server-baseline/component-plan.example.json) 建立本活动的
组件计划。样例故意把 NeoForge `1.21.11` forwarding 标记为 `blocked`：接手者必须实时
盘点并批准实际实现，不能因为指标模组已经存在就误认为身份转发也已经解决。

本案例后端只应包含活动共同 JAR、NeoForge 对应指标模组和获批的 forwarding 实现。
Velocity Authorizer 留在代理；Lobby Guard 和 LuckPerms Tier Agent 留在内部大厅；
ServerControlAgent、StatusCollector、世界备份和告警通过主机目标注册，不复制到 `mods`。

组件计划通过审查后，在 owl5 的无秘密服控模板中增加独立目标，不能覆盖现有
`activity`、`fanstreet` 或 `yugong`：

```json
{
  "serverId": "activity-ready-check",
  "serverDirectory": "E:\\Activities\\ready-check",
  "startTaskName": "Hechao-Server-Activity-ReadyCheck",
  "port": 25568,
  "conflictGroup": "owl5-activity-slot",
  "logRelativePath": "logs\\latest.log",
  "propertiesRelativePath": "server.properties",
  "memorySettingsRelativePath": "user_jvm_args.txt",
  "maximumAllowedMemoryMiB": 8192,
  "allowedCommandPrefixes": ["list", "save-all", "say", "whitelist"]
}
```

部署代理配置前必须让对应单元测试验证：目标目录和任务非空、`25568` 共享者都有同一
冲突组、内存文件真实包含唯一 `-Xms/-Xmx`。安装任务、代理和控制台桥后先只读盘点，
不要把“目标出现于后台”误认为服务端应该自动启动。

## 9. 后台目录记录

示例目录记录：

```text
id: activity-ready-check
displayName: 集合签到
status: Maintenance
maxPlayers: 20
minecraftVersion: 1.21.11
loader: NeoForge
minimumTier: Participant
clientProfileId: activity-ready-check-neoforge-1.21.11
velocityTarget: activity
allowsProtocolTranslation: false
role: Player
monitoringEnabled: true
```

先保持 `Maintenance`。只有客户端档案、后端启动、心跳、指标和管理员真实进服全部通过
后才设为 Online。同一时间，其他共享 `velocityTarget=activity` 的记录必须不可进入。

## 10. 一次完整演练

1. 提交 `hechao-ready-check` 源码和测试，生成 `0.1.0` JAR 与 SHA-256。
2. 本地专用服务端和两客户端通过；记录观察者拒绝和重复包结果。
3. 构建独立客户端档案 `1.0.0`，离线验签并完成对象闭合校验。
4. 上传缺失 OSS 对象；后台导入签名清单并按 Test、Gray、Production 推进。
5. 新活动目录保持 Maintenance；旧活动停止签发授权并等待玩家清空。
6. 备份当前活动世界和配置，部署新后端到独立目录，保持停止。
7. 获得管理员明确授权后，从后台启动 `activity-ready-check`；冲突组自动先停旧后端。
8. 核对 `25568` 只有新 PID、日志版本正确、心跳和 TPS/MSPT/GC 新鲜。
9. 管理员通过启动器选择“集合签到”，使用 fresh grant 进入；错误档案必须失败。
10. 将新记录设为 Online，旧记录保持 Maintenance，依次执行 `2/3/5/20` 人灰度。
11. 发布证据、Git 提交和标签推送后才宣布上线。

## 11. 回滚演练

故意让 Test 客户端协议改为 `2`，验证旧服务端清楚拒绝。随后：

1. 目录改为 Maintenance；
2. 客户端通道回退到协议 `1` 的清单；
3. 若服务端也已升级，停止新后端并恢复 `0.1.0`；
4. 经明确授权重新启动，确认 `25568`、心跳和协议；
5. 管理员 fresh grant 重新进入后恢复 Online。

回滚全过程不得启动第二个活动后端，不得把玩家送到 Survival2 或大厅，也不得覆盖已经
发布的协议 `2` 对象和标签。

## 12. Codex 应交付的结果

该案例完成时，接手 Codex 应给出：

- 活动源码提交、测试数、JAR 版本和 SHA-256；
- `profileId`、档案版本、清单 SHA-256 和发布通道状态；
- `serverId`、目录、计划任务、端口、冲突组和是否启动；
- 1/2/7/20 人结果与 TPS/MSPT/GC；
- 管理员进服、错误协议拒绝、观察者拒绝和重复包证据；
- 客户端通道回滚和服务端回滚目标；
- 未完成的真实玩家验收，不能用“代码完成”替代。

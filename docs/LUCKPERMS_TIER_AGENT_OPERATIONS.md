# LuckPerms 全局等级代理运维

> 当前加载版本：`HechaoLuckPermsTierAgent 0.1.2`
>
> 目标 API：`0.30.2`（协议自 `0.16.0` 起保持兼容）
>
> 生产状态：`0.1.2` 已于 2026-08-14 通过内部大厅隔离重启加载；部署后至少三轮五分钟只读同步成功。下一次真实等级变更仍需跨两轮同步确认不回退
>
> 边界：只修改四个固定全局组；部署只重启内部大厅，不操作 Velocity、生存服或活动服

## 1. 设计

管理后台不能直接修改 LuckPerms 的 MariaDB。直接写库会绕过 LuckPerms 缓存、消息服务和
在线玩家刷新，因此 API 只在 PostgreSQL 中创建带预期主组的变更命令。大厅 Paper 插件
异步领取命令，再通过 LuckPerms API 修改玩家的全局继承节点并保存用户。

固定映射如下：

| 赫朝等级 | LuckPerms 主组 |
| --- | --- |
| `Member` | `default` |
| `Participant` | `vip` |
| `Collaborator` | `admin` |
| `Administrator` | `owner` |

插件没有可注册命令，也没有任意组名或任意控制台命令入口。它会显式更新 LuckPerms 的
持久化主组，再移除上述四个组的全局继承节点并增加目标组；带上下文的节点和其他业务组
保持不变。

## 2. 并发与失败边界

- 管理员提交时必须携带当前 LuckPerms 主组，主组已变化则返回 `409`。
- 同一玩家同一时间只能有一条 `Pending` 或 `Claimed` 命令。
- 每次领取生成 90 秒租约并递增 `attemptCount`。
- 回执必须同时匹配代理 ID 和 `attemptCount`，旧进程或旧租约的迟到回执返回 `409`。
- API 回执失败时租约到期后会重领；目标已经生效会按幂等成功处理。
- `User#getPrimaryGroup()` 是按 LuckPerms 配置计算的结果，不是 SQL 中的 stored primary
  group。代理不使用该值判断 stored value 冲突；计算结果无论是预期组、目标组还是第三组，
  都必须执行受控收敛。并发保护由 API 入队事务、快照修订和单玩家待处理约束承担。
- 代理始终通过 `loadUser` 读取存储状态，不复用单服旧缓存；保存成功后必须通过已配置的
  LuckPerms messaging service 广播用户更新。消息服务不可用时故障关闭。
- 代理只回传固定错误码，不把异常正文、令牌或数据库信息写入 API。
- 当前管理员不能修改自身等级，最后一个可用管理员不能被降级。

迁移 `13 / luckperms_tier_change_commands` 保存命令状态、租约、观察到的主组和失败码。
排队及完成分别写入 `luckperms.tier_change.queued` 和
`luckperms.tier_change.completed` 审计。

## 3. 配置

配置文件位于：

```text
E:\LobbyServer\plugins\HechaoLuckPermsTierAgent\config.properties
```

关键设置：

```properties
api-base-url=https://launcher-api.hechao.world/
agent-id=owl5-lobby
request-timeout-seconds=10
poll-interval-seconds=10
claim-limit=10
```

`token` 使用已有内部同步令牌。安装脚本从本机 DPAPI
`C:\ProgramData\Hechao\LauncherBridge\sync-token.dat` 解密，并只把插件配置授权给
`SYSTEM`、本机管理员组和执行安装的管理员。不得把配置文件、令牌或解密输出提交到 Git。

## 4. 构建与安装

### 4.1 `0.1.1` 持久化修复

`0.1.0` 只替换全局继承节点，并在保存后读取计算得到的 `User#getPrimaryGroup()` 作为
成功依据。LuckPerms 的该值受主组计算策略影响，不等同于 `players.primary_group` 的
持久化 stored value，因此代理可能回执 `Applied`，而下一轮只读同步仍读到旧主组。

`0.1.1` 在替换节点前调用 `User#setPrimaryGroup`。调用被 LuckPerms 拒绝时返回固定错误码
`primary_group_update_failed`，不再保存节点或误报成功。自动测试必须覆盖 stored value
更新、节点替换、保存顺序和拒绝分支。

### 4.2 `0.1.2` 计算主组与跨服缓存修复

生产使用 `primary-group-calculation: parents-by-weight`。当目标继承节点已经存在时，
`User#getPrimaryGroup()` 可能已经返回目标组，即使 `players.primary_group` 仍保存旧值。
`0.1.1` 会在此处提前返回 `Applied`，没有再次调用 `User#setPrimaryGroup`；其他服务器也没有
收到用户更新广播。下一轮只读快照因此仍从 MariaDB 读到旧 stored value 并恢复后台身份。

`0.1.2` 按以下顺序执行：

1. 从 LuckPerms 存储加载用户，不使用单服现有缓存作为写入基线。
2. 确保目标全局继承节点存在，并移除其余三个受管全局组节点；任一节点操作失败时先恢复
   本机内存状态，再故障关闭。带上下文节点和其他业务组保持不变。
3. 调用 `User#setPrimaryGroup`；计算主组已经等于目标时也不提前成功。
4. 等待 `saveUser` 完成，再调用 `MessagingService.pushUserUpdate` 通知其他实例重新加载。
5. 保存或广播异常时回执失败，并立即从存储重新加载本机用户，避免大厅缓存停留在半修改
   状态。

LuckPerms API 不提供 stored primary group 读取接口，因此插件内不使用
`User#getPrimaryGroup()` 伪造持久化读回。真实验收必须由既有只读同步脚本直接查询
MariaDB `players.primary_group`，并至少跨过两轮五分钟同步。

构建：

```powershell
.\src\Hechao.VelocityAuthorizer\gradlew.bat `
  -p src\Hechao.LuckPermsTierAgent clean test jar --no-daemon
```

安装：

```powershell
.\deploy\windows\luckperms-tier-agent\Install-LuckPermsTierAgent.ps1 `
  -JarPath .\src\Hechao.LuckPermsTierAgent\build\libs\HechaoLuckPermsTierAgent-0.1.2.jar
```

脚本会：

1. 校验 JAR 和 DPAPI 令牌。
2. 把既有代理 JAR 与配置备份到 `E:\manual-backups`。
3. 通过临时文件、SHA-256 校验和原子改名部署 JAR。
4. 写入并收紧配置 ACL。
5. 返回 `ServerRestartPerformed=false`。

安装不会启动或重启大厅。磁盘上的 JAR 替换成功不代表修复已经生效；插件必须经过内部
大厅隔离重启和日志门禁后才算加载。重启前后必须记录全部 Java PID，且不得操作 Velocity
或其他游戏服务。

2026-08-14 部署前实时核验：

```text
E:\LobbyServer\plugins\HechaoLuckPermsTierAgent-0.1.1.jar
SHA-256 F3B8871D55914CD403987A4AAEF901F1AF6FC12B44395FCACDFE99BB8C0AA450
E:\LobbyServer\plugins\HechaoLuckPermsTierAgent\config.properties
```

配置 ACL 继承已关闭，明文令牌不得进入终端输出或 Git。安装脚本会为本次替换创建新的
`E:\manual-backups\luckperms-tier-agent-*` 回滚点；安装前后 Java PID 集合必须一致，且
返回 `ServerRestartPerformed=false`。

2026-08-14 正式部署结果：

```text
E:\LobbyServer\plugins\HechaoLuckPermsTierAgent-0.1.2.jar
SHA-256 917984C1DED705F38F3BF768518A1011C7EF974E9BEB322BCEB7BB4CE07A364E
回滚目录 E:\manual-backups\luckperms-tier-agent-20260814T005515Z
```

安装阶段五个 Java PID 全部未变；随后通过既有控制台桥确认 `save-all flush`，只重启
`Hechao-Server-Lobby`。大厅从旧 PID `7924` 切换到新 PID `9480`，其余四个 Java
进程的 PID、启动时间和路径均未变化。日志确认 `0.1.2` 加载、启用并到达 `Done`；
自动回滚未触发。PID 和实时运行状态属于 2026-08-14 09:05 CST 的部署证据，后续操作前
必须重新核验。

## 5. 验收与回滚

自动验收覆盖 HTTPS 配置限制、轮询、成功回执、回执失败后的幂等重试、API 输入限制、
租约序号冲突，以及生产备份还原后的排队、领取、迟到回执拒绝、完成和快照更新。

实际加载后检查：

1. 日志出现 `LuckPerms tier agent enabled as owl5-lobby`。
2. 后台对测试玩家执行 `default -> vip`。
3. 直接读取 MariaDB stored primary group，并确认后台等级和权限均更新。
4. 等待至少两轮五分钟只读同步，确认 stored primary group 与后台身份没有恢复旧值。
5. 再执行 `vip -> default` 恢复测试账号，并再次等待同步确认。
6. 检查两条排队/完成审计，确认日志中没有令牌或请求正文。

截至 2026-08-14 09:18 CST，`0.1.2` 加载后的 `01:08`、`01:13`、`01:18 UTC`
三轮五分钟同步均返回成功，117 条 stored primary group 快照被正常接收，等级命令队列
保持为空。历史最近十条命令涉及三名真实玩家，不是隔离测试账号，因此部署任务没有为
凑验收而擅自修改或降级任何玩家。下一次管理员按正常业务提交等级变更时，仍须按上述
步骤核对 MariaDB stored value、API 身份与两轮同步后的稳定性；在该证据形成前，不把
真实变更闭环标为完成。

回滚时停服后把当前 JAR 移出 `plugins`，从最近
`E:\manual-backups\luckperms-tier-agent-*` 恢复旧 JAR 与配置。迁移 13 是加法变更，
旧 API 会忽略命令表；未完成命令不得在不核对 LuckPerms 实际主组的情况下手工删除。

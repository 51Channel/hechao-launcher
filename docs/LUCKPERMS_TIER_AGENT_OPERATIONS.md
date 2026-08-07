# LuckPerms 全局等级代理运维

> 当前加载版本：`HechaoLuckPermsTierAgent 0.1.1`
>
> 上线验收：插件加载、隔离重启、轮询与健康检查已通过；真实玩家往返读回待完成
>
> 目标 API：`0.28.7`（协议自 `0.16.0` 起保持兼容）
>
> 生产状态：磁盘与大厅进程均为 `0.1.1`；完成真实玩家改级、两轮只读同步和反向恢复前，不得声明业务缺陷已闭环
>
> 边界：只修改四个固定全局组，不执行控制台命令，不启动、停止或重启任何服务器

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

构建：

```powershell
.\src\Hechao.VelocityAuthorizer\gradlew.bat `
  -p src\Hechao.LuckPermsTierAgent clean test jar --no-daemon
```

安装：

```powershell
.\deploy\windows\luckperms-tier-agent\Install-LuckPermsTierAgent.ps1 `
  -JarPath .\src\Hechao.LuckPermsTierAgent\build\libs\HechaoLuckPermsTierAgent-0.1.1.jar
```

脚本会：

1. 校验 JAR 和 DPAPI 令牌。
2. 把既有代理 JAR 与配置备份到 `E:\manual-backups`。
3. 通过临时文件、SHA-256 校验和原子改名部署 JAR。
4. 写入并收紧配置 ACL。
5. 返回 `ServerRestartPerformed=false`。

安装不会启动或重启大厅。磁盘上的 JAR 替换成功不代表修复已经生效；插件只有在获得明确
授权并重启大厅后才会加载。重启前后必须记录全部 Java PID，且不得操作 Velocity 或其他
游戏服务。

2026-07-27 已部署：

```text
E:\LobbyServer\plugins\HechaoLuckPermsTierAgent-0.1.0.jar
SHA-256 35A9BBB17620DC2FD7245E0EA8CCAA293DC98C264DA3463AB706846ED7E42A7B
E:\LobbyServer\plugins\HechaoLuckPermsTierAgent\config.properties
```

配置 ACL 继承已关闭，明文令牌未进入终端输出或 Git。备份位于
`E:\manual-backups\luckperms-tier-agent-20260726T223127Z`。安装前后 Java PID
集合一致，返回 `ServerRestartPerformed=false`。

## 5. 验收与回滚

自动验收覆盖 HTTPS 配置限制、轮询、成功回执、回执失败后的幂等重试、API 输入限制、
租约序号冲突，以及生产备份还原后的排队、领取、迟到回执拒绝、完成和快照更新。

实际加载后检查：

1. 日志出现 `LuckPerms tier agent enabled as owl5-lobby`。
2. 后台对测试玩家执行 `default -> vip`。
3. LuckPerms 主组、后台等级和权限均更新。
4. 再执行 `vip -> default` 恢复测试账号。
5. 检查两条排队/完成审计，确认日志中没有令牌或请求正文。

回滚时停服后把当前 JAR 移出 `plugins`，从最近
`E:\manual-backups\luckperms-tier-agent-*` 恢复旧 JAR 与配置。迁移 13 是加法变更，
旧 API 会忽略命令表；未完成命令不得在不核对 LuckPerms 实际主组的情况下手工删除。

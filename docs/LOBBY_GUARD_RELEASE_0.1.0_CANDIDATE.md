# Lobby Guard 0.1.0 候选发布记录

> 状态：历史候选记录；已由
> [`LOBBY_GUARD_RELEASE_0.1.0.md`](LOBBY_GUARD_RELEASE_0.1.0.md)
> 的正式生产记录取代
>
> 适用服务端：`E:\LobbyServer`

## 1. 作用

Lobby Guard 是大厅后端独立于 Velocity 和 API 的最后一道玩家入口保护：

- 在 `AsyncPlayerPreLoginEvent` 最终阶段拒绝连接。
- 在 Paper `PlayerConnectionValidateLoginEvent` 再次拒绝连接。
- 插件启用时若已有玩家，会立即将其移出。
- 不注册玩家命令，不读取或修改 LuckPerms，不影响指标、备份、控制台和计划任务。
- 管理员、OP 和普通玩家一律不能进入。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoLobbyGuard-0.1.0.jar` | `3,047` | `B0B7AA651994797B16B1271D332EF03A218F8BB8FEC3226CF0F705D74311DE99` |

## 3. 自动验证

- Java 测试 `3/3` 通过。
- JAR 已核对包含 `plugin.yml`、主插件类和登录监听器。
- 生产安装器通过 PowerShell 语法解析。

## 4. 生产安装行为

[`Install-HechaoLobbyGuard.ps1`](../tools/server/Install-HechaoLobbyGuard.ps1)
会：

1. 确认 Velocity 的 `lobby` 后端为 `127.0.0.1:25566`。
2. 确认大厅正在运行且没有玩家连接。
3. 备份旧守卫、`server.properties`、白名单和哈希清单。
4. 通过控制桥执行 `save-all flush` 和 `stop`。
5. 安装 JAR，设置 `server-ip=127.0.0.1`、`white-list=true`、
   `enforce-whitelist=true`，并写入空白名单。
6. 启动 `Hechao-Server-Lobby`，验证 `Done`、守卫日志、本机监听和零玩家连接。

任一步失败会自动恢复旧插件和其余配置，但仍保留本机监听、强制白名单和空白名单这一
安全下限。完整旧文件只保存在备份中，不会被自动用于重新开放大厅。

## 5. 生产门槛

- 从 Velocity、后端地址、旧 `/hub`/`/lobby`/`/l` 和 NPC 路径均不能进入大厅。
- Authorizer 停止或 API 超时时，守卫仍拒绝登录。
- 大厅 LuckPerms 代理、TPS/MSPT/GC、心跳、告警和正式备份继续正常。

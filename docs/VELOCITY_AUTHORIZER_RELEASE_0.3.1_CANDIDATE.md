# Velocity 授权插件 0.3.1 候选记录

> 状态：仅部署到回环隔离 Velocity，未部署生产。
>
> 源码提交：`6d98fabf11960aa3ed790f5f7cc004cb0866fd62`
>
> 当前生产仍为：`HechaoVelocityAuthorizer-0.3.0.jar`

## 1. 修复

`0.3.0` 只在首次连接时记录 API 返回的 `serverId`。PVP 成功返回大厅后，会话来源
仍错误保留为 `pvp`；后续转服可能使用旧来源执行客户端兼容判断。

`0.3.1` 改为记录每一次实际放行后的服务器：

- 正常转服使用 API 返回的目标 `serverId`；
- `monitor` 模式因 API 不可用、普通策略拒绝或响应缺少 `serverId` 而放行时，记录
  实际请求目标；
- 初始授权指向未注册目标且 `monitor` 保留原路由时，记录原始目标；
- `enforce` 模式遇到 API 不可用或允许响应缺少 `serverId` 时继续关闭失败。

这样 PVP -> Lobby -> PVP 的第二次判定会以真实 Lobby 为来源，不能用陈旧的 PVP
来源绕过反向版本隔离。

## 2. 测试与制品

- Velocity Java 测试 `20/20`，失败、错误和跳过均为 `0`。
- 覆盖允许转服、三类 monitor 放行、未注册初始目标、enforce API 故障和缺失
  `serverId`。

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoVelocityAuthorizer-0.3.1.jar` | `21,215` | `2FC06C2DBE6F01AFAC2C5AA016C902A10B4B1675C876C5850630B726BB041E75` |

JAR 内 `velocity-plugin.json` 的 ID 为 `hechao-velocity-authorizer`，版本为 `0.3.1`，
主类为 `world.hechao.velocityauth.HechaoVelocityAuthorizer`。

## 3. 隔离部署

候选只安装到 `E:\Velocity-PvpReturn-Staging`。安装器要求隔离任务停止、端口
`25579` 无监听，并在操作前后核对生产 PID、配置哈希、生产 `0.3.0` 哈希和生产
Via JAR 数量。

- 旧隔离 JAR 备份：
  `E:\manual-backups\PvpReturnAuthorizerStaging-20260728T040947Z`
- 候选启用 JAR 数量：`1`
- 隔离核心：Velocity `4.0.0` build `6`，SHA-256
  `4540289F48C83E305FC2F2C495A84D1F4D0B7F360830251E169DD5A208740E70`
- 隔离运行时：Temurin Java `25.0.4+7`，仅供隔离任务使用
- 隔离进程：仅监听 `127.0.0.1:25579`
- 隔离代理只加载 Authorizer 与 HubCommand；代理启用 Via 数量为 `0`
- 大厅后端加载 ViaVersion/ViaBackwards `5.11.0`，启用数量为 `2`，哈希受管理脚本固定
- API 受限凭据探针：`PlayerNotLinked`
- 协议 `393`、`763`、`774` 状态握手全部通过
- 启动日志 fatal/error：`0`

生产 Velocity 仍为 PID `472`，配置 SHA-256 仍为
`A300E7CBE190B42E434763CFCCAFB9D821F894B02E72A594ED72B340C3E22C70`，
生产 Authorizer 仍为 `0.3.0`，生产 Via 启用数量仍为 `0`。

## 4. 剩余门槛

正确恐怖整蛊 1.20.1 正版客户端已完成首次授权、代理路由和后端登录。第一次
`/hub` 已连接 Lobby 后端，但旧隔离 Velocity `3.4.0-SNAPSHOT` 在切服时发生
`accept_teleportation` 协议状态解码错误；命令、API 授权、网络可达性和 Lobby
登录均不是失败点。

隔离核心随后先升级为 Velocity `3.5.1` build `615`。真实复测能够进入 Lobby 并收到
权限与欢迎信息，但仍因 `accept_teleportation` 多出 `16` 字节而断开。隔离任务现已
使用独立 Java 25 启用 Velocity `4.0.0` build `6`。进一步核对发现隔离代理与大厅
后端同时加载了同一组 ViaVersion/ViaBackwards，违反 ViaVersion 只在代理或后端
其中一处安装的要求。隔离代理的 Via JAR 已改为 `.disabled`，统一由大厅后端承担
转换；代理现加载三个插件、启用 Via 数量为 `0`，大厅启用 Via 数量为 `2`。
回环监听、`393/763/774` 状态探测与管理脚本远端 `Status` 均通过。生产 Velocity 和
所有游戏服未重启。机器证据见
[`PVP_RETURN_REAL_SESSION_2026-07-28.json`](evidence/PVP_RETURN_REAL_SESSION_2026-07-28.json)。

修正后的首条 PVP -> `/hub` -> Lobby 真实会话已通过并稳定超过 `591` 秒。四段日志
解码错误为 `0`，两个后端的名称与 UUID 内存比对一致，聊天、LuckPerms、TPS/MSPT
和短窗口 GC 均通过。当前仍须完成皮肤/背包/移动目视、反向转服拒绝、再次签发进入
PVP、断线重连和正常退出。完整验收前不得替换生产 `0.3.0`，不得启用生产 Via，也
不得切换 `enforce`。

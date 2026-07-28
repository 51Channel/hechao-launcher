# PVP Velocity modern 接入运维

> 当前状态：FabricProxy-Lite `2.6.0` 与 CrossStitch `0.1.6` 已部署到
> `owl9`，配置与 `owl5` Velocity 的 modern forwarding 密钥一致。正确 1.20.1
> 档案第一次真实路由暴露后端自定义包解码失败；CrossStitch 修复后的持久任务已完成
> 真实统一入口、身份转发、直连拒绝、稳定连接和正常退出验收。一次后续重连在进入
> Velocity 认证前超时，仍须补做干净重连；跨版本返回大厅已完成默认关闭的代码设计。
> 回环隔离代理已完成 1.13/1.20.1/1.21.11 status 协议协商，受限凭据 API
> 探针通过；API `0.21.0` 也已在生产数据库副本上通过默认关闭、目标定向和模组
> 档案防绕过验收。仍待真实登录、`/hub` 和生产灰度。

## 1. 生产拓扑

```text
玩家
  -> mc.hehe11.fun
  -> owl5 Velocity 3.4, modern forwarding
  -> owl9.vipi9.top:19243
  -> C:\mc\server, Fabric 1.20.1, internal port 25565
```

PVP 后端继续使用 `online-mode=true`。FabricProxy-Lite 的
`hackOnlineMode=true` 让后端接受携带有效 modern forwarding 数据的 Velocity
连接，同时拒绝没有有效转发数据的普通直连。不得把后端改成离线模式。

## 2. 已安装基线

| 项目 | 值 |
| --- | --- |
| 模组 | `C:\mc\server\mods\FabricProxy-Lite-2.6.0.jar` |
| 模组 SHA-256 | `D4719179353D790453061C14B4148994FF431AC57A126555B3009CE9A748D6C7` |
| 模组大小 | `209342` 字节 |
| 兼容模组 | `C:\mc\server\mods\crossstitch-0.1.6.jar` |
| 兼容模组 SHA-1 | `aba735301c683ed43d5f3361f532bf38f28116f2` |
| 兼容模组大小 | `5321` 字节 |
| 配置 | `C:\mc\server\config\FabricProxy-Lite.toml` |
| 配置 SHA-256 | `47B33719CE4CC18F0AE25C50859255CBD1A19853D45163882DDF5F8515E25EA5` |
| 配置设置 | `hackOnlineMode=true`、`hackEarlySend=true`、`hackMessageChain=true` |
| 服务端 | Minecraft `1.20.1`、Fabric Loader `0.16.14`、Java 21 |
| 启动任务 | `HorrorPrank`，手动触发；2026-07-28 核心验收时为 `Running` |
| 部署备份 | `C:\manual-backups\pvp-velocity-modern-20260727T172218Z` |
| CrossStitch 变更备份 | `C:\mc\manual-backups\pvp-crossstitch-20260728T003311Z` |

安装包来自 FabricProxy-Lite 官方 Modrinth 项目，对应 Git 标签 `v2.6.0`
和提交 `258d0ee20f618b94cfa65eb58fb3b84e32011648`。该版本声明支持
Minecraft `1.20.1`、Fabric Loader `0.14.21+` 和 Java `17+`。现有
Fabric API 已包含它要求的 `fabric-networking-api-v1`。

CrossStitch 使用官方 Modrinth `0.1.6`。Velocity 官方兼容说明建议内容型 Fabric
服务端安装该兼容层，以处理代理无法原生理解的自定义 Brigadier 参数类型。第一次
真实 PVP 路由在 Velocity 侧出现后端包解码失败，服务端玩家随即断开；安装后启动日志
同时列出 CrossStitch `0.1.6`、FabricProxy-Lite `2.6.0` 并在 `12.293` 秒完成。
随后使用计划任务 `HorrorPrank` 持久启动，SSH 会话退出后任务、Java 进程与
`25565` 监听均继续存在。正确赫朝客户端经统一入口被定向到 `pvp`，后端稳定连接
`586` 秒，期间没有新的无效包或解码错误；语音认证、后端缓存和最新玩家数据与启动
身份的内存哈希比对一致。公网直连探针收到 `velocity:player_info` 挑战，并在拒绝
转发数据后得到 `This server requires you to connect with Velocity.`。客户端最终
正常关闭，启动器记录退出码 `0`。

第一次后续重连在统一入口读取阶段超时，未进入 Velocity 认证，也未到达 PVP 后端；
客户端仍停留在主菜单并由管理员正常关闭，退出码仍为 `0`。因此核心 modern
forwarding 已通过，但“断线后再次成功进入”仍是单独的未完成门槛。

配置文件在部署前已经存在。安装器核对全部安全设置和密钥摘要后复用原文件，
没有改写密钥或配置内容，只把 ACL 收紧为 `SYSTEM` 与本机
`Administrators`。`owl5` 的 `E:\Velocity\forwarding.secret` 也使用相同权限；
内容哈希、Velocity PID `6068` 和两个 Velocity 计划任务定义在加固前后保持不变。

## 3. 密钥边界

转发密钥只允许存在于以下位置：

- `owl5`：`E:\Velocity\forwarding.secret`
- `owl9`：`C:\mc\server\config\FabricProxy-Lite.toml`
- 两台主机各自受限的运维备份目录

不得把密钥明文写入 Git、聊天、命令参数、普通临时文件或证据 JSON。
部署脚本只接受 UTF-8 Base64 数据的标准输入，并在内存中核对 SHA-256。
跨主机部署必须由受控运维进程读取源文件、在内存中编码并写入远端标准输入。

部署脚本：

```text
deploy/windows/pvp-velocity/Install-PvpVelocityForwarding.ps1
```

脚本执行前强制检查 PVP Java 进程和内部 `25565` 监听均为空；执行后也不会启动
服务器。它会核对官方 JAR 描述、服务端 `online-mode=true`、配置安全项、密钥摘要、
启动脚本和计划任务定义，并在写入前创建受限备份。

## 4. 当前验收状态

已完成：

1. 使用原有 `HorrorPrank` 任务持久启动，没有使用随 SSH 会话结束的临时子进程。
2. FabricProxy-Lite `2.6.0` 与 CrossStitch `0.1.6` 同时加载，后端完成启动。
3. `owl9-pvp` 心跳与后端监听在线，采集器所有权保持在 `owl9-pvp`。
4. 已绑定 Minecraft 的真实账号从统一 Velocity 入口稳定进入 PVP。
5. 启动 UUID 与后端语音、缓存和玩家数据身份一致；证据只保留布尔结果，不保存
   UUID 或指纹。名称、皮肤和有效游戏内权限仍需专用账号目视核对。
6. 公网直连被 modern forwarding 明确拒绝。
7. Velocity、PVP 与客户端同一时间窗口的日志已脱敏留证，正常退出码为 `0`。

仍需完成：

1. 再做一次成功断线重连，排除已记录的统一入口单次超时。
2. 使用专门验收账号目视核对皮肤和有效游戏内权限。
3. 验证 API 短暂失败以及与当前档案兼容的 NPC/命令转服路径。
4. PVP 为 `1.20.1`，大厅为 `1.21.11`；当前 ViaVersion/ViaBackwards 禁用，
   授权层也会硬拒绝版本不匹配。目标服级协议转换开关已在开发分支实现且默认关闭，
   回环隔离代理的三档协议状态握手、受限凭据探针及 API `0.21.0` 数据库副本门槛
   已通过，仍须按
   [`PVP_RETURN_ROUTE_DESIGN.md`](PVP_RETURN_ROUTE_DESIGN.md) 完成真实登录、
   `/hub` 和生产灰度，现有 `/hub` 仍不得记为通过。

只有剩余步骤全部通过后，才可把 PVP 全路径记为“生产验收完成”。在此之前，
Velocity 继续保持 `monitor`，不得因为静态文件已部署就切换 `enforce`。

## 5. 回滚

回滚前必须由管理员优雅关闭 PVP，并再次确认没有 Java 进程和 `25565` 监听。
若只回滚本次兼容修复，移出 `crossstitch-0.1.6.jar` 并使用
`C:\mc\manual-backups\pvp-crossstitch-20260728T003311Z` 复核。若回滚整个 modern
forwarding，再移出 `FabricProxy-Lite-2.6.0.jar`；原配置在没有模组时不会生效，
可以保留其加固后的 ACL。逐字节恢复前必须使用对应受限备份核对。

回滚不得修改 `online-mode=true`，不得启动服务器，也不得改变 owl5 的转发密钥。

机器可读证据见
[`evidence/OWL9_PVP_VELOCITY_MODERN_DEPLOYMENT_2026-07-28.json`](evidence/OWL9_PVP_VELOCITY_MODERN_DEPLOYMENT_2026-07-28.json)
与
[`evidence/CLIENT_ACTIVITY_PVP_ROUTE_AND_PVP_FIX_2026-07-28.json`](evidence/CLIENT_ACTIVITY_PVP_ROUTE_AND_PVP_FIX_2026-07-28.json)。

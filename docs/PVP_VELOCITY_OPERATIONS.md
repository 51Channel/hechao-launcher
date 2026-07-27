# PVP Velocity modern 接入运维

> 当前状态：FabricProxy-Lite `2.6.0` 已部署到 `owl9`，配置与
> `owl5` Velocity 的 modern forwarding 密钥一致。PVP 仍由服主手动启动，
> 本次部署没有启动、停止或重启任何游戏进程。真实路由尚待下一次手动开服验收。

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
| 配置 | `C:\mc\server\config\FabricProxy-Lite.toml` |
| 配置 SHA-256 | `47B33719CE4CC18F0AE25C50859255CBD1A19853D45163882DDF5F8515E25EA5` |
| 配置设置 | `hackOnlineMode=true`、`hackEarlySend=true`、`hackMessageChain=true` |
| 服务端 | Minecraft `1.20.1`、Fabric Loader `0.16.14`、Java 21 |
| 启动任务 | `HorrorPrank`，手动触发，当前保持 `Ready` |
| 部署备份 | `C:\manual-backups\pvp-velocity-modern-20260727T172218Z` |

安装包来自 FabricProxy-Lite 官方 Modrinth 项目，对应 Git 标签 `v2.6.0`
和提交 `258d0ee20f618b94cfa65eb58fb3b84e32011648`。该版本声明支持
Minecraft `1.20.1`、Fabric Loader `0.14.21+` 和 Java `17+`。现有
Fabric API 已包含它要求的 `fabric-networking-api-v1`。

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

## 4. 下一次手动开服验收

以下步骤只能在服主选择的窗口执行：

1. 服主使用原有方式手动启动 `HorrorPrank`，运维自动化不得代为启动。
2. 从服务端日志确认 FabricProxy-Lite `2.6.0` 加载成功，没有依赖或 mixin 错误。
3. 确认 `owl9-pvp` 心跳把 `pvp` 更新为在线，采集器所有权没有回到 `owl5`。
4. 使用已绑定 Minecraft 的测试账号从统一 Velocity 入口进入 PVP。
5. 核对玩家 UUID、名称、皮肤和权限与正版身份一致。
6. 直接连接 `owl9.vipi9.top:19243` 必须被拒绝，不能绕过 Velocity。
7. 验证 PVP 返回大厅、断线重连和 API 短暂失败路径。
8. 保留 Velocity、PVP 和 API 同一时间窗口的脱敏日志。

只有这些步骤全部通过后，才可把 PVP 路由记为“生产验收完成”。在此之前，
Velocity 继续保持 `monitor`，不得因为静态文件已部署就切换 `enforce`。

## 5. 回滚

回滚前必须由服主手动关闭 PVP，并再次确认没有 Java 进程和 `25565` 监听。
最小回滚是移出 `FabricProxy-Lite-2.6.0.jar`；原配置在没有模组时不会生效，
可以保留其加固后的 ACL。若需要逐字节恢复部署前状态，使用受限备份目录中的
`prechange.json`、原配置、启动脚本和任务 XML 核对后再原子恢复。

回滚不得修改 `online-mode=true`，不得启动服务器，也不得改变 owl5 的转发密钥。

机器可读证据见
[`evidence/OWL9_PVP_VELOCITY_MODERN_DEPLOYMENT_2026-07-28.json`](evidence/OWL9_PVP_VELOCITY_MODERN_DEPLOYMENT_2026-07-28.json)。

# 天域远征工业季 Arclight 启动与进服修复

- 日期：`2026-08-16`
- 目标：owl5 独立生存槽 `activity-survival`
- 服务端目录：`E:\HechaoActivitySlots\activity-survival`
- Minecraft：`1.21.1`
- 服务端核心：Arclight NeoForge `1.0.2-SNAPSHOT-8086b06`
- 内部端点：`127.0.0.1:25600`
- Velocity 目标：`activity-survival`

## 故障现象

工业季客户端档案 `skyrealm-industrial-neoforge-1.21.1 / 1.0.11` 已能正常启动，
启动器也能创建指向 `activity-survival` 的一次性进服授权，但玩家经统一 Velocity
入口连接时被拒绝。Velocity 的直接原因是后端没有请求 modern forwarding 数据。

## 根因

服务端目录虽然包含 Arclight JAR，并且 `arclight.conf` 已启用 Velocity forwarding，
原 `start.bat` 却绕过 Arclight，直接执行 NeoForge 生成的 `win_args.txt`：

```bat
java @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.228/win_args.txt nogui
```

这个启动方式只加载 NeoForge 原生服务端，不会加载 Arclight、Bukkit 插件或 Arclight
的 Velocity forwarding mixin。因此配置文件本身正确也不会生效，Velocity 会把该后端
视为未启用 modern forwarding。

## 生产变更

在受管控制台确认目标世界完成 `save-all flush` 后，只停止并重新启动
`activity-survival`。没有启停其他 Minecraft 后端，也没有重启 Velocity。

`E:\HechaoActivitySlots\activity-survival\start.bat` 改为从 Arclight JAR 启动：

```bat
java @user_jvm_args.txt -jar arclight-neoforge-1.21.1-1.0.2-SNAPSHOT-8086b06.jar nogui
```

| 项目 | SHA-256 |
| --- | --- |
| 修改前 `start.bat` | `4968FDF46085DEB4FD948D20BC305A7A18448DA03403C5BE56F5134EDF35D1B7` |
| 修改后 `start.bat` | `29C9DA5BC508B666A54632BB0A968BE930EE770CA61ECEF300B1E2B39F69E976` |

正式回滚点：

```text
E:\manual-backups\activity-survival-arclight-start-20260816T054137Z
```

此前 forwarding 配置备份继续保留在：

```text
E:\manual-backups\activity-survival-arclight-forwarding-20260816T052201Z
```

备份和文档均不包含 forwarding secret 明文。

## 验证结果

### 服务端启动

- 计划任务 `Hechao-Server-activity-survival` 为 `Running`；
- Java 命令行明确包含目标 Arclight JAR；
- `127.0.0.1:25600` 由同一 Java PID 监听；
- 日志出现 `Done (4.240s)` 和 `Running on Bukkit - Arclight`；
- LuckPerms、Vault、SkyrealmCore、Chunky、GriefPrevention 等 Bukkit 插件实际加载；
- 最终只读复核时，最新日志尾部没有启动失败、崩溃、看门狗终止或内存溢出。

### 正版玩家进服闭环

使用正式启动器服务、真实 Microsoft/Minecraft 会话和
`skyrealm-industrial-neoforge-1.21.1 / 1.0.11` 完成验收：

1. `13:44:31 +08:00` 创建目标为 `activity-survival` 的 fresh grant；
2. `13:44:55 +08:00` 由 `owl5-main` 消费同一授权；
3. 后端取得与正版会话一致的玩家 UUID，并收到经 Velocity 转发的原始非回环来源地址；
4. `13:44:57 +08:00` 服务端记录玩家登录和 `joined the game`；
5. 玩家稳定在线约 `150` 秒，服务端 `list` 持续可见该玩家；
6. 客户端由验收器正常停止，退出码为 `0`；服务端记录 `Disconnected` 和
   `left the game`，工业季服务端继续运行；
7. 本地验收进程和 Minecraft 进程均已退出，没有残留 `running-game.json`。

结构化证据不保存玩家名称、完整 UUID、公网 IP、授权 ID、令牌、Cookie 或会话材料。

## 非阻断告警

本次启动日志仍包含下列已知兼容告警，但它们没有阻止服务端 ready 或真实玩家进服：

- Sable 相关模组在专用服务端扫描客户端类时产生 DistCleaner 记录；
- 两条 Dungeons Arise advancement 加载失败；
- EssentialsX `2.21.0` 提示当前混合核心不在正式支持范围；
- TAB `5.0.7` 的 NMS 注入兼容警告；
- Simple Voice Chat 的独立 UDP 认证尚未完成。

这些问题不属于本次 Velocity 进服故障，本轮未扩大范围修改。

## 回滚

回滚必须在确认目标服无人并完成世界保存后进行：

1. 只停止 `activity-survival`；
2. 从正式回滚点恢复原 `start.bat`；
3. 复核恢复哈希为
   `4968FDF46085DEB4FD948D20BC305A7A18448DA03403C5BE56F5134EDF35D1B7`；
4. 保持目标服停止，除非管理员明确要求启动。

回滚会重新引入“绕过 Arclight、无法接收 Velocity forwarding”的原故障，因此正常
运维不应执行该回滚。重新部署工业季服务端包时，也必须确保 `start.bat` 继续通过
Arclight JAR 启动。

机器可读证据见
[`evidence/SKYREALM_INDUSTRIAL_ARCLIGHT_START_FIX_2026-08-16.json`](evidence/SKYREALM_INDUSTRIAL_ARCLIGHT_START_FIX_2026-08-16.json)。

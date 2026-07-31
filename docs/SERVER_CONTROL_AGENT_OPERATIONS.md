# 赫朝服务端控制代理运维手册

> 状态：已于 2026-07-31 在 owl5、owl9 与生产 API 启用；后台已显示 9 个目标、
> 2 个代理和 5 个运行中实例，三项管理员启停操作均已成功。
>
> 适用范围：管理员 Web 后台、API 控制队列、Windows 游戏 VPS 本机代理。
>
> 所有 Windows 命令统一使用 PowerShell 7：`pwsh.exe`。

## 1. 安全边界

服控功能只面向完成独立后台会话与 TOTP 验证的管理员。玩家启动器不会获得
代理令牌，也不能调用服控接口。

本机代理只支持以下结构化动作：

- 启动一个配置中明确列出的计划任务；
- 先执行 `save-all flush`，再通过 Minecraft 控制台执行 `stop`；
- 修改 `server.properties` 中五个白名单字段并保留备份；
- 发送配置中明确允许的单行 Minecraft 命令。

代理不提供 PowerShell、CMD、SSH、任意文件浏览或任意进程终止接口。控制台输入
会在 API 和本机代理两层检查；`stop`、`restart`、`shutdown`、`end` 永远不能作为
普通终端命令发送，停服只能走带二次确认和审计的结构化动作。

## 2. 冲突服编排

共享同一端口、活动入口或其他独占资源的服务端必须使用相同
`conflictGroup`。例如同一台主机上占用 `25565` 的替换服应放入一个冲突组。

管理员启动或重启目标服时，API 会：

1. 锁定目标及同组状态；
2. 拒绝代理离线、心跳过期或已有动作的目标；
3. 为所有在线冲突服排入序号 `0` 的优雅停止动作；
4. 等待全部停止动作成功；
5. 仅在全部成功后执行序号 `1` 的目标启动动作；
6. 任一停止失败时取消后续启动，并记录失败审计。

代理在本机再次检查冲突状态。共用本机端口的服务端还必须由受管启动任务写入运行
标记；代理会核对标记、任务进程祖先和监听端口所有者。端口被不明进程占用时会返回
`LOCAL_PORT_OCCUPIED` 并拒绝启动，不会猜测进程归属。

## 3. 快捷设置

后台可修改：

- `max-players`
- `view-distance`
- `simulation-distance`
- `difficulty`
- `white-list`

代理以临时文件和同卷替换写入，并在
`C:\ProgramData\Hechao\ServerControlAgent\backups` 保存原文件。设置不会触发
自动重启；需要重启生效的项目由管理员另行执行受控重启。

## 4. Minecraft 控制台

后台显示 `logs\latest.log` 的受限尾部快照，最多 `64 KiB`。控制台只允许每台
服务器配置中的命令前缀，建议首批仅开放：

```text
list
save-all
say
whitelist
```

控制台桥接任务必须位于登录中的 Administrator 桌面会话，因为 Windows SSH 或
SYSTEM 会话不能直接附加到可见 Java 控制台。桥接实现和人工应急流程见
[`MINECRAFT_SERVER_CONTROL_OPERATIONS.md`](MINECRAFT_SERVER_CONTROL_OPERATIONS.md)。

## 5. 部署前盘点

生产配置不得复制示例路径。每台 VPS 必须逐项确认：

1. 服务器 ID 与后台目录 ID 完全一致；
2. 服务端目录、`server.properties`、日志和启动批处理真实存在；
3. 端口与当前监听一致；
4. 共享端口和替换服的冲突组完整；
5. 启动任务只对应一个服务端；
6. 控制台桥接任务已在正确桌面会话安装；
7. 停服前世界备份和恢复路径可用。

owl9 的历史 Velocity 目标 `pvp` 实际是
`C:\mc\server` 的恐怖整蛊服；真正 PVP 是
`E:\MinecraftServer`。两者共享 `25565`，必须作为两个独立目标放在同一冲突组，
不能互换目录或启动任务。

2026-07-30 的实时只读盘点还确认：

- owl5 的 `ActivityNeoForge`、`FanStreet` 与 `Yugong` 都使用 `25568`，统一放入
  `owl5-activity-slot`；
- owl5 的 `Survival2` 与 `DollNight` 都使用 `25565`，统一放入
  `owl5-survival-slot`；
- owl9 的历史 `pvp` 与真正 PVP 统一放入 `owl9-25565-slot`。

仓库中的
[`server-control-agent.owl5.production.json`](../deploy/windows/server-control/server-control-agent.owl5.production.json)
和
[`server-control-agent.owl9.production.json`](../deploy/windows/server-control/server-control-agent.owl9.production.json)
是本次实时盘点形成的无密钥白名单。旧迁移目录没有自动纳入控制目标；需要重新启用
时必须先在后台建立清晰的服务端 ID，再复核端口和冲突组。

## 6. 安装顺序

先发布 API，但保持 `ServerControl:Enabled=false`。数据库迁移可先执行，旧接口和
现有游戏进程不受影响。

然后在每台 Windows VPS 上：

1. 将 `deploy/windows/server-control` 下的固定脚本部署到
   `C:\ProgramData\Hechao\ServerControl`；
2. 安装 Minecraft 控制台桥；
3. 让现有启动批处理支持 `HECHAO_MANAGED_START`；
4. 为每个真实目标重建受管启动任务，并明确传入 `-ServerId`；
5. 用 `New-ServerControlAgentToken.ps1` 生成一次性随机令牌文件和 SHA-256；
6. 将 SHA-256 配到 API，将 DPAPI `LocalMachine` 密文留在对应 VPS；
7. 发布代理单文件 EXE，并用 `Install-ServerControlAgent.ps1` 校验哈希、配置、
   任务和运行标记参数；
8. 先启动代理但不执行游戏服动作，只核对心跳、目标、端口和日志；
9. 所有目标只读状态正确后再启用 API 服控开关。

API 主机统一通过
[`configure-server-control.sh`](../deploy/linux/configure-server-control.sh)
写入总开关、心跳时效、命令租约和各代理令牌摘要。脚本会先备份环境文件，且启用时
至少要求一个合法摘要。摘要不是明文令牌，但仍不得写入 Git、聊天或发布记录。

示例任务安装：

```powershell
pwsh.exe -NoLogo -NoProfile -File `
  C:\ProgramData\Hechao\ServerControl\Install-MinecraftServerLaunchTask.ps1 `
  -ServerName Survival2 `
  -ServerId survival2 `
  -ServerDirectory E:\Survival2
```

### 6.1 接管已经运行的旧任务

部分服务端在服控代理上线前已经由旧版 `Run-MinecraftServer.ps1` 启动。为了避免仅
因接入后台而重启在线服，先更新下一次启动使用的受管任务，再运行
[`Adopt-MinecraftServerRuntime.ps1`](../deploy/windows/server-control/Adopt-MinecraftServerRuntime.ps1)。

第一次必须带 `-WhatIf`。脚本只在以下条件全部成立时才允许写运行标记：

1. 指定计划任务当前确实处于运行状态；
2. 任务动作明确包含目标服务端根目录；
3. 指定端口只有一个监听进程且进程是 Java；
4. Java 的祖先进程链中存在包含同一根目录的 `pwsh`、PowerShell 或 `cmd` 运行器。

正式执行只原子写入服务 ID、运行器 PID、启动时间和目录，不发送控制台命令，也不
启动、停止或重启服务端。既有标记与当前运行实例不一致时默认硬拒绝；只有完成独立
复核后才允许 `-Replace`。如果祖先链无法验证，就保持该目标不可控制，等下次计划
维护时由新版受管任务正常启动。

示例配置
[`server-control-agent.example.json`](../deploy/windows/server-control/server-control-agent.example.json)
仅展示字段和冲突组，不代表任何生产路径。

### 6.2 生产状态（2026-07-31）

生产 API `0.23.1` 已启用 `ServerControl`，owl5 和 owl9 的代理计划任务均为
`Running`。数据库实时核对结果为：

- 受管服务器 `9` 个；
- 30 秒内在线代理 `2` 个；
- 运行中目标 `4` 个；
- 待处理或历史服控操作 `0` 个，代理命令 `0` 个。

本次部署只重启 `Hechao Launcher Server Control Agent` 计划任务。安装器返回
`server_action=none`，owl5 的 Lobby、Survival2、Activity 以及 owl9 的恐怖整蛊服
在部署前后 PID 保持不变。代理制品 SHA-256 为
`FE09808A3C075AFAF9523691E26A07C71B8E1FAC62B9DAFD0C3968D7613DE70C`，源码提交为
`e9faeff301d7512a03eef4dcc40b3cfa46f43b02`。完整证据见
[`evidence/SERVER_CONTROL_PRODUCTION_DEPLOYMENT_2026-07-31.json`](evidence/SERVER_CONTROL_PRODUCTION_DEPLOYMENT_2026-07-31.json)。

### 6.3 管理员动作验收（2026-07-31）

生产后台在完成 MFA 后已正常渲染 9 个服务器目标，不再显示空状态。管理员随后发起
三项操作，API、代理与 VPS 进程读回结果一致：

- `survival1` 启动成功，PID 为 `2576`；
- `pvp`（恐怖整蛊）停止成功，原 PID `7216` 已不存在；
- `pvp-purpur`（真正 PVP）启动成功，PID 为 `2912`。

三条命令均为一次尝试成功，当前无进行中操作。当前生产为 9 个目标、2 个在线代理、
5 个运行中实例、3 条已完成操作和 3 条已完成命令。本次 Codex 只做数据库、页面和
进程读回，没有发起或撤销这些管理员操作。结构化重启、快捷设置、终端命令白名单
与冲突组自动先停后启仍需分别验收。完整证据见
[`evidence/SERVER_CONTROL_PRODUCTION_ACTION_ACCEPTANCE_2026-07-31.json`](evidence/SERVER_CONTROL_PRODUCTION_ACTION_ACCEPTANCE_2026-07-31.json)。

## 7. 验收与回滚

首轮只使用专用、无玩家测试目标验证：

1. 代理离线时按钮不可用；
2. 错误二次确认不能排队；
3. 允许和拒绝的控制台命令符合配置；
4. 快捷设置写入、备份和恢复正确；
5. 冲突服停止失败时目标服绝不启动；
6. 共享端口的不明占用被拒绝；
7. 完成记录和管理员审计一致。

不得用生产玩家服作为首次启停验收目标。

发生异常时先将 `ServerControl:Enabled` 设回 `false`。这会立即禁止新动作，不影响
已运行的 Minecraft 进程。随后停止代理计划任务并恢复安装脚本生成的配置、EXE 和
计划任务备份。代理不会强杀 Java，也不会在失败后自动重启已停止的冲突服；这种
失败关闭行为用于防止两个冲突服务同时运行。

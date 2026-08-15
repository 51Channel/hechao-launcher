# 赫朝服务端控制代理运维手册

> 状态：生产基线为 API `0.32.0`、owl5 代理 `0.7.0` 与 owl9 代理 `0.4.0`。
> 当前登记 9 个目标和 2 个在线代理。运行实例数来自实时心跳，写操作前必须实时核验。
> 动态槽尚未创建；固定 `activity` 与全部游戏服的运行状态在写操作前必须实时核验。
>
> 适用范围：管理员 Web 后台、API 控制队列、Windows 游戏 VPS 本机代理。
>
> 所有 Windows 命令统一使用 PowerShell 7：`pwsh.exe`。

## 1. 安全边界

服控功能只面向完成独立后台会话与 TOTP 验证的管理员。玩家启动器不会获得
代理令牌，也不能调用服控接口。

本机代理只支持以下结构化动作：

- 启动一个配置中明确列出的计划任务；
- 受管启动和控制台桥都会关闭 Windows QuickEdit，避免鼠标选择控制台文本时冻结
  Java 输出；
- 受管启动会把 stdout/stderr 直接追加到
  `C:\ProgramData\Hechao\ServerControlAgent\logs\<serverId>-console.log`，达到
  `64 MiB` 后保留一份 `previous` 日志，避免 Task Scheduler 的未消费输出管道填满；
- 先执行 `save-all flush`，再通过 Minecraft 控制台执行 `stop`；若同一受管
  Java PID 在 20 秒后仍监听目标端口，则再次关闭该控制台的 QuickEdit，并发送一次
  `Ctrl+C`，触发 JVM 关机钩子并继续等待正常释放；
- 修改 `server.properties` 中五个白名单字段并保留备份；
- 读取并修改每服显式声明的 JVM `-Xms/-Xmx` 启动内存，按单服上限校验；
- 对固定 `activity` 或已就绪的生存、活动、PVP、小游戏动态槽执行带租约、摘要和文件清单的
  `DeployPackage`，原子切换服务端目录并保持停止；
- 从固定 `activity` 安全模板创建 `survival-*`、`activity-*`、`pvp-*`、`minigame-*`
  动态槽，创建独立目录、批准端口、固定文件快照和无触发器计划任务；不接受管理员指定
  任意路径、端口或启动命令；
- 发送配置中明确允许的单行 Minecraft 命令。

代理不提供 PowerShell、CMD、SSH、任意文件浏览或任意进程终止接口。`Ctrl+C`
兜底只存在于带二次确认和审计的结构化停止动作中；发送前必须重新确认监听进程仍是
最初的受管 Java PID，PID 变化时硬拒绝，不会改为强杀。控制台输入
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

每个目标必须同时声明 `memorySettingsRelativePath` 和
`maximumAllowedMemoryMiB`。内存文件可以是 `start.bat`、`start.ps1` 或
NeoForge 的 `user_jvm_args.txt`，但必须恰好包含一个 `-Xms` 和一个 `-Xmx`；
代理遇到缺失、重复、越界或无法解析时会失败关闭。后台以 GiB 展示和输入，协议使用
MiB，最小值为 `512 MiB`、步长为 `256 MiB`，并由代理再次核对单服硬上限。

代理以临时文件和同卷替换写入，并在
`C:\ProgramData\Hechao\ServerControlAgent\backups` 保存 `server.properties`
和内存参数文件的原件。两份文件作为同一次设置事务处理；任一写入失败时会自动恢复
原始字节。设置不会触发
自动重启；需要重启生效的项目由管理员另行执行受控重启。

## 4. Minecraft 控制台

后台显示 `logs\latest.log` 的受限尾部快照，最多 `64 KiB`。`allowedCommandPrefixes`
既可以列出具体命令前缀，也可以使用唯一通配值 `*`。当前生产策略为所有目标使用
`["*"]`，即允许全部 Minecraft、模组和插件命令；新建独立槽从固定 `activity` 模板
继承同一策略。以下生命周期命令始终不通过自由控制台发送：

```text
stop
restart
shutdown
end
```

生命周期命令必须使用后台“停止”或“重启”按钮，让代理先保存世界、等待端口释放、处理
冲突组并留下完整审计。API、后台和本机代理都会识别 `*`，旧版 API 或代理不接受该值，
因此启用通配模式时必须先升级 API，再升级对应代理。

控制台桥接任务必须位于登录中的 Administrator 桌面会话，因为 Windows SSH 或
SYSTEM 会话不能直接附加到可见 Java 控制台。桥接实现和人工应急流程见
[`MINECRAFT_SERVER_CONTROL_OPERATIONS.md`](MINECRAFT_SERVER_CONTROL_OPERATIONS.md)。

## 5. 部署前盘点

生产配置不得复制示例路径。每台 VPS 必须逐项确认：

1. 服务器 ID 与后台目录 ID 完全一致；
2. 服务端目录、`server.properties`、日志、启动批处理和显式内存参数文件真实存在；
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

2026-07-31 的只读内存基线如下。前两列来自真实启动文件，最后一列是后台允许设置的
单服硬上限，不代表主机可以同时把所有服务端都开到上限：

| 代理 | 服务端 | 当前 Xms | 当前 Xmx | 单服上限 |
| --- | --- | ---: | ---: | ---: |
| owl5 | `lobby` | 1 GiB | 2 GiB | 4 GiB |
| owl5 | `survival1` | 0.5 GiB | 2 GiB | 6 GiB |
| owl5 | `survival2` | 1 GiB | 2 GiB | 6 GiB |
| owl5 | `dollnight` | 4 GiB | 11 GiB | 12 GiB |
| owl5 | `activity` | 2 GiB | 6 GiB | 8 GiB |
| owl5 | `fanstreet` | 2 GiB | 6 GiB | 8 GiB |
| owl5 | `yugong` | 2 GiB | 6 GiB | 8 GiB |
| owl9 | `pvp`（恐怖整蛊） | 2 GiB | 5 GiB | 6 GiB |
| owl9 | `pvp-purpur`（真正 PVP） | 2 GiB | 4 GiB | 6 GiB |

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
至少要求一个合法摘要；整合包部署命令使用独立的 180 分钟租约。摘要不是明文令牌，
但仍不得写入 Git、聊天或发布记录。

示例任务安装：

```powershell
pwsh.exe -NoLogo -NoProfile -File `
  C:\ProgramData\Hechao\ServerControl\Install-MinecraftServerLaunchTask.ps1 `
  -ServerName Survival2 `
  -ServerId survival2 `
  -ServerDirectory E:\Survival2
```

`Run-MinecraftServer.ps1` 支持主机级 `HECHAO_JAVA_HOME`。计划任务环境没有全局
`java` 时，runner 先读取当前进程值，再读取机器值，校验 `bin\java.exe` 后只在该次
受管启动中设置 `JAVA_HOME` 并把 `bin` 放到 `PATH` 首位。变量未配置时保持旧行为；
变量已配置但路径无效时在执行服务端批处理前失败关闭。

owl5 使用统一 Java 21 时，以管理员 PowerShell 7 设置机器值：

```powershell
[Environment]::SetEnvironmentVariable(
    'HECHAO_JAVA_HOME',
    'E:\jdk',
    [EnvironmentVariableTarget]::Machine
)
```

更新后先使用只运行 `java -version` 的临时受管批处理验证，禁止把生产玩家服作为首次
探针。回滚时恢复上一版 `Run-MinecraftServer.ps1`，并把 `HECHAO_JAVA_HOME` 恢复为
变更前的值；已运行的 Java 进程不受该变量或 runner 文件替换影响。
owl5 活动槽首次生产修复与并行 runner 回滚点见
[`SERVER_CONTROL_RUNNER_MANAGED_JAVA_2026-08-14.md`](SERVER_CONTROL_RUNNER_MANAGED_JAVA_2026-08-14.md)。

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

### 6.2 生产状态（2026-08-02）

生产 API `0.26.2` 已启用 `ServerControl`。当前 owl5 代理为 `0.2.4`，owl9 代理为
`0.2.1`，两台代理计划任务均为 `Running`。已接入 `9` 个受管目标，运行中目标数会随
管理员手动启停动态变化，不应写成固定开放状态。

`0.2.0` 新增 JVM 内存读写与单服硬上限；`0.2.1` 将心跳和命令拆为独立循环；
`0.2.3` 在 owl5 修复受管 stdout 管道堵塞和空服无法关停；`0.2.4` 让两个循环在日志
写入失败或未知单次异常后继续运行。九个目标均上报 `Xms`、`Xmx` 和
`maximumAllowedMemoryMiB`；应用设置不会自动重启服务端。

owl5 当前代理制品 SHA-256 为
`9BAE24B2B5A5491B7A926661D37B2BA806599C5164C0F83C1307B4D25449301E`，源码提交为
`b0b10140a3fb68b067987e2ddfc2f3b48ff682d5`。内存基线见
[`evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_ACCEPTANCE_2026-07-31.json`](evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_ACCEPTANCE_2026-07-31.json)；
当前 owl5 发布见 [`SERVER_CONTROL_AGENT_RELEASE_0.2.4.md`](SERVER_CONTROL_AGENT_RELEASE_0.2.4.md)，
owl9 仍以 [`SERVER_CONTROL_AGENT_RELEASE_0.2.1.md`](SERVER_CONTROL_AGENT_RELEASE_0.2.1.md)
为正式版本。

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

### 6.4 目录实际状态同步（API 0.24.1）

目录记录保持管理员策略，服控代理负责提供具体物理服的实际运行状态。配置为 `Online` 的目标在代理新鲜上报在线时开放，上报停止时自动关闭，代理失联时故障关闭；重新运行并恢复心跳后自动开放。该机制不会自动启动或停止 Minecraft，也不会用共享 Velocity 入口代替具体物理服判断。

当前 `activity`、`pvp` 等活动目录应保持 `Online` 策略，是否对玩家开放由同名服控目标决定。`pvp` 仍代表恐怖整蛊，真正 PVP 的服控目标是 `pvp-purpur`。生产证据见 [`API_RELEASE_0.24.1.md`](API_RELEASE_0.24.1.md) 和 [`evidence/CATALOG_SERVER_CONTROL_AVAILABILITY_ACCEPTANCE_2026-07-31.json`](evidence/CATALOG_SERVER_CONTROL_AVAILABILITY_ACCEPTANCE_2026-07-31.json)。

### 6.5 心跳与长命令隔离（代理 0.2.1）

代理 `0.2.1` 为心跳和命令建立两个独立异步循环。停止脚本、启动脚本或其他长命令只占用命令循环，不再阻塞同一代理管理的其他目标心跳。生产升级后 20 秒窗口内两台代理均多次推进心跳，最大观测间隔为 owl5 `10.1` 秒、owl9 `7.2` 秒；这包含九个目标的串行状态采集时间，不应解释为固定 5 秒写库周期。

本次未向生产游戏服下发长命令，只验证了独立调度测试、真实生产持续心跳和升级前后 Java PID/启动时间不变。若要执行破坏性停止链路验收，必须使用无玩家隔离目标。完整证据见 [`evidence/SERVER_CONTROL_AGENT_0.2.1_PRODUCTION_DEPLOYMENT_2026-07-31.json`](evidence/SERVER_CONTROL_AGENT_0.2.1_PRODUCTION_DEPLOYMENT_2026-07-31.json)。

### 6.6 空服关停阻塞修复（owl5 代理 0.2.3）

2026-07-31，Activity NeoForge 在 `pause-when-empty-seconds=60` 触发空服暂停后，
`save-all flush` 和 `stop` 无法被主线程处理。线程栈确认主线程阻塞在
`TerminalConsoleAppender -> FileOutputStream.write`；根因是计划任务继承的 stdout
管道无人持续消费，写满后 Java 输出永久阻塞。QuickEdit 不是该次永久阻塞的最终原因。

关服前已完成 VSS 世界备份并核对 `level.dat` 和 SHA-256；目标最终保持停止。owl5 随后
升级到代理 `0.2.3`，受管 stdout/stderr 直接写入轮换日志，并保留 `save-all -> stop ->
Ctrl+C` 的结构化停止链。owl9 继续运行 `0.2.1`。升级只重启服控代理，Activity 任务始终
为 `Ready`，`25568` 无监听，其余 5 个 Java PID 和启动时间不变。

本地受管启动探针验证退出码、stdout/stderr、`64 MiB` 轮换和运行标记清理共 `4/4`；
按照手动开服边界，没有为生产验收重新启动 Activity。发布与证据见
[`SERVER_CONTROL_AGENT_RELEASE_0.2.3.md`](SERVER_CONTROL_AGENT_RELEASE_0.2.3.md) 和
[`evidence/SERVER_CONTROL_AGENT_0.2.3_PRODUCTION_DEPLOYMENT_2026-07-31.json`](evidence/SERVER_CONTROL_AGENT_0.2.3_PRODUCTION_DEPLOYMENT_2026-07-31.json)。

### 6.7 代理循环自恢复（owl5 代理 0.2.4）

`0.2.4` 将本机日志改为尽力写入，并使用统一恢复循环运行心跳和命令轮询。日志目录
不可写、日志轮换失败或未预见的单次异常不再终止代理；正常取消仍会立即结束，两个循环
仍相互独立。调度与日志故障回归为 `26/26`，完整解决方案为 `578/578`。

owl5 升级只重启代理计划任务，代理 PID 从 `7436` 变为 `8848`，五个 Java PID
`2576 / 6008 / 7748 / 9428 / 10412` 未变化。最终数据库快照显示 owl5 `7` 个目标中
`3` 个运行、owl9 `2` 个目标中 `1` 个运行，九个目标均无过期心跳；本发布以来服控操作
和待处理操作均为 `0`。发布与证据见
[`SERVER_CONTROL_AGENT_RELEASE_0.2.4.md`](SERVER_CONTROL_AGENT_RELEASE_0.2.4.md) 和
[`evidence/SERVER_CONTROL_AGENT_0.2.4_PRODUCTION_DEPLOYMENT_2026-08-02.json`](evidence/SERVER_CONTROL_AGENT_0.2.4_PRODUCTION_DEPLOYMENT_2026-08-02.json)。

### 6.8 整合包停服部署（owl5 代理 0.3.0）

`0.3.0` 只允许配置中显式启用 `packageDeploymentEnabled` 的目标领取 `DeployPackage`。
当前 API 进一步固定为 `activity / owl5 / 25568 / owl5-activity-slot`，其他目标即使伪造
管理员请求也会被拒绝。部署前后均重新核对目标没有受管 Java PID；归档通过摘要、大小、
数量、解压总量和安全路径校验后，才进入同卷暂存目录。

活动目标必须声明 `startScriptRelativePath=start.bat`，且脚本与计划任务同时满足
`HECHAO_MANAGED_START` 契约。`forwarding.secret` 等主机固定文件只能从旧受控目录复制；
世界仅在管理员明确选择时保留。目录切换失败会自动恢复旧版本，成功后保留一个受控
回滚目录并保持停服。完整流程见
[`PACKAGE_IMPORT_OPERATIONS.md`](PACKAGE_IMPORT_OPERATIONS.md)。`0.3.0` 已只部署到 owl5，
owl9 保持 `0.2.1`；升级服控代理时五个既有 Minecraft Java PID 未变化，活动服没有
启动。首轮固定试包的客户端 `Test` 发布成功，但活动目录切换遇到 Windows 文件占用；
旧目录自动恢复且正式通道未变化。

### 6.9 心跳与目录切换互斥（owl5 代理 0.3.1）

生产复现确认心跳与命令轮询虽然运行在独立循环，但心跳会读取活动目录内的
`server.properties`、JVM 参数和控制台尾部。Windows 在子文件句柄短暂打开时拒绝父目录
重命名，导致 `0.3.0` 第二次试包在切换阶段失败。

`0.3.1` 为每个目标加入异步目录访问门闩：心跳快照、快捷设置事务和整合包恢复/切换
阶段互斥，整合包下载、摘要校验与暂存解压不持有门闩，因此不会长期阻塞全机心跳。
配置文件读取同时允许读写与删除共享；目录和保留文件移动只对 Windows 瞬时共享、锁定
以及父目录被打开子项占用的等价错误做 `50/100/200/400/800 ms` 有界重试，持续失败仍
进入既有自动回滚。真实父目录重命名并发、错误分类、重试上限和编辑器读取均已加入
Windows 回归。代理专项测试 `46/46`、完整解决方案 `633/633` 和 Release 构建零警告
通过。owl5 已部署产品版本
`0.3.1+784c05d8ba172a594a8d95c47c14db253e1cb53a`，正式 EXE SHA-256 为
`9229F5C7B69C1FC7D35DE10C3BE4B750A519970A5F42BDC54AB32EB3724C8FA9`；owl9 未升级。

### 6.10 固定试包与原目录恢复（2026-08-05）

第一次 CLI 升级尝试在文件替换阶段失败，安装器自动恢复 `0.3.0`、重新运行代理并保持
五个 Java PID 不变；修正升级器后，第二次从 `0.3.0` 原子升级到 `0.3.1` 成功。升级
仅重启服控代理，活动任务始终为 `Ready`，`25568` 未监听。

`0.3.1` 的首次生产部署尝试仍被一个 2026-07-12 由旧计划任务产生的孤立
`cmd.exe /d /c start.bat` 阻塞。该进程位于 Session 0、没有窗口和 Java 子进程，但持有
`E:\ActivityNeoForge` 目录句柄。代理没有绕过文件锁，而是失败并自动恢复旧目录。使用
Microsoft 签名有效的 Sysinternals Handle 确认唯一占用者后，只终止该孤立包装进程；
游戏 Java 进程没有变化。

随后固定试包 `b4620e53-f125-4749-b220-101d17189cc4` 一次完成客户端 Test-only 发布和
停止活动槽部署。部署后原活动服按受控 owner 原子恢复，原树摘要一致；无秘密测试目录
归档后清理。最终活动目录为 `326` 个文件、`212,626,569` 字节，回滚目录、owner、
测试临时目录均不存在。服控代理恢复为单实例 `Running`，活动任务为 `Ready`，
`25568` 无监听，五个 Java PID 仍为
`2576 / 6008 / 7748 / 9428 / 10412`。

API 最终读回为 owl5 七个目标全部报告 `0.3.1`，心跳新鲜，活动目标离线；进行中的
owl5 命令、操作和整合包任务均为 `0`。发布和恢复证据见
[`SERVER_CONTROL_AGENT_RELEASE_0.3.1.md`](SERVER_CONTROL_AGENT_RELEASE_0.3.1.md)、
[`evidence/SERVER_CONTROL_AGENT_0.3.1_PRODUCTION_DEPLOYMENT_2026-08-05.json`](evidence/SERVER_CONTROL_AGENT_0.3.1_PRODUCTION_DEPLOYMENT_2026-08-05.json)
与
[`evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json`](evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json)。

### 6.10 停服后永久删除服务端文件（代理 0.4.0）

管理后台可以对代理配置中显式设置 `serverDeletionEnabled: true` 的一次性活动目标发起
`DeleteServerFiles`。该动作只删除目标的 `serverDirectory`，包括其中的世界、模组、
插件、配置和日志；不会删除代理状态、计划任务、VPS 外置备份、OSS 客户端或目录数据库
记录。删除并完成清理后，目标从日常服控面板隐藏，但保留审计和代理配置；固定
`activity` 槽仍在整合包页显示，可用于后续重新部署。

安全门禁如下：

1. 只有管理员后台会话可提交，必须填写 4 到 500 个字符的原因；
2. 必须精确输入 `DELETE <serverId>`，普通服务器 ID 确认不被接受；
3. API 要求代理在线、目标已停服、没有进行中的控制命令且代理心跳明确开放删除；
4. 代理在移动目录前再次检查受管进程，拒绝磁盘根目录、重解析点、代理状态目录和包含
   其他受管服务端的父目录；
5. 运行目录先在同卷原子移到带命令 ID 的暂存目录，再做不跟随重解析点的递归清理；
6. 清理受文件占用阻挡时，运行目录仍保持移除，心跳报告“后台清理中”并继续重试；
7. 已完成命令由本机收据安全重放，目录已不存在时返回幂等成功；
8. 删除成功后不会启动服务器，也不会自动归档目录服务器记录。

生产基线只对 `dollnight`、`activity`、`fanstreet`、`yugong` 和 owl9 恐怖整蛊目标开放。
大厅、两个生存服和长期 PVP 保持关闭。新增目标必须逐项审核目录边界后才能打开该开关。

### 6.11 VPS 内存上报与整合包建议（owl5 代理 0.4.1）

owl5 `0.4.1` 使用 Windows `GlobalMemoryStatusEx` 上报 VPS 真实物理内存。API `0.28.5`
将该值保存为可空字段，并在整合包页显示 VPS 总内存、推荐最小值和推荐最大值。推荐
区间只用于帮助管理员判断，不是服务端内存上限；区间外的合法值仍可提交。

推荐最小值按物理内存八分之一计算并限制在 `4-8 GiB`，推荐最大值按一半计算并限制在
`1-16 GiB`，两者均向下对齐 `256 MiB`。owl5 当前上报 `18431 MiB`，对应推荐
`4096-8960 MiB`。结构化命令仍拒绝小于 `1 GiB`、大于 `64 GiB` 或不是
`256 MiB` 整数倍的输入；这属于格式和技术边界，不是推荐上限。

本版本只升级 owl5，owl9 没有整合包部署槽并保持 `0.4.0`。升级只重启服控代理自身，
五个 Java 进程身份未变化。见
[`SERVER_CONTROL_AGENT_RELEASE_0.4.1.md`](SERVER_CONTROL_AGENT_RELEASE_0.4.1.md) 和
[`evidence/PACKAGE_MEMORY_GUIDANCE_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/PACKAGE_MEMORY_GUIDANCE_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

### 6.12 活动槽部署身份（owl5 代理 0.5.0）

`0.5.0` 从活动目录受控 `.hechao-deployment.json` 读取实际部署的 `importId`、
`profileId` 和 `version`，并随目标心跳上报。标记缺失、格式无效、目录不存在或目标未启用
整合包部署能力时，身份保持空，不根据目录名称或最近操作猜测。

Launcher API `0.30.0` 把该身份保存到 `server_control_targets`。活动企划只有在当前时间
进入 `[开始, 结束)`、活动槽在线、代理心跳新鲜且 `deployed_package_import_id` 与企划
绑定 import 完全相同时才对目录和 Velocity 显示 `Online`。这可以防止上一场整合包仍
留在活动槽时误开放下一场。部署身份只用于准入校验，不会自动启动、停止或切换服务端。

代理已于 2026-08-10 只在 owl5 升级。五个 Java PID、启动时间和规范化路径与升级前
完全一致；活动任务为 `Ready`、`25568` 无监听，活动目录和运行标记均不存在。完整发布、
回滚和隐私证据见 [`SERVER_CONTROL_AGENT_RELEASE_0.5.0.md`](SERVER_CONTROL_AGENT_RELEASE_0.5.0.md)
与 [`ACTIVITY_PLAN_OPERATIONS.md`](ACTIVITY_PLAN_OPERATIONS.md)。

### 6.13 独立部署槽（owl5 代理 0.7.0）

`0.7.0` 扩展结构化 `CreateDeploymentSlot`。管理员选择 `Activity`、`Survival`、`Pvp`、
`Minigame` 并提交对应 `activity-*`、`survival-*`、`pvp-*`、`minigame-*` ID、显示名、
固定模板 ID 和原因。API 使用串行化事务检查目录与服控 ID 未占用、模板代理新鲜且具有
部署能力，从 `25600-25611` 分配未占用端口，再写入 `Provisioning` 槽和带租约命令。
API 与生产配置上限均为 `12` 个 `Provisioning / Ready` 槽；失败记录不占用额度。

owl5 代理只从固定 `activity` 安全模板派生主机配置、内存文件、固定文件和世界路径；
每个动态槽使用 API 批准的独立端口、槽自身的 Velocity 目标和空冲突组。目录固定为
`E:\HechaoActivitySlots\<serverId>`，任务固定为 `Hechao-Server-<serverId>`。代理先检查
端口未被配置或真实监听占用、根目录不是重解析点，并拒绝覆盖已有目录或任务，再创建
owner 标记、复制主机固定文件、写入目标端口、安装无触发器计划任务并原子持久化。
安装失败或取消会清理本轮目录、任务、快照和状态；成功后 API 只在心跳同时确认正确
代理、独立端口、空冲突组和部署能力时把槽标为 `Ready`。

动态槽在有效 `.hechao-deployment.json` 出现前启停接口返回 `DEPLOYMENT_REQUIRED`。
它默认停止、隐藏且不进入玩家目录；部署成功也不会自动启动。不同动态槽没有共享冲突组，
可以同时运行。固定 `activity / 25568 / owl5-activity-slot` 仍只约束共享旧活动入口的
替换服。代理安装器在替换旧 EXE 前校验动态槽配置与任务安装脚本，升级本身只重启代理，
不操作 Minecraft。

该能力已于 2026-08-15 在 owl5 正式上线；API `0.32.0` 心跳确认代理 `0.7.0` 和八个
目标新鲜上报。工业季已迁移为停止的 `Survival / 25600 / activity-survival` 独立槽，
固定活动服继续由原 PID 监听 `25568`。发布与回滚证据见
[`SERVER_CONTROL_AGENT_RELEASE_0.7.0.md`](SERVER_CONTROL_AGENT_RELEASE_0.7.0.md)。

## 7. 验收与回滚

首轮只使用专用、无玩家测试目标验证：

1. 代理离线时按钮不可用；
2. 错误二次确认不能排队；
3. 允许和拒绝的控制台命令符合配置；
4. 快捷设置写入、备份和恢复正确；
5. 冲突服停止失败时目标服绝不启动；
6. 共享端口的不明占用被拒绝；
7. 完成记录和管理员审计一致。
8. 整合包只能部署到停止的 owl5 固定活动槽或已就绪动态槽，且成功与失败均不自动开服；
9. 固定文件、世界保留、启动脚本、重解析点和目录切换回滚分别通过。
10. 运行中的目标、未开放删除能力的目标和错误 `DELETE <serverId>` 均被拒绝；
11. 删除只影响配置的运行目录，外置备份保持存在；
12. 删除后启动按钮不可用，整合包重新部署后才能恢复；
13. 清理失败状态、命令重放和目录已不存在状态保持幂等。
14. 活动槽心跳只在有效受控部署标记存在时上报精确部署身份；篡改或不完整标记保持空。
15. 当前企划绑定 import 与活动槽部署身份不一致时，目录和 Velocity 均故障关闭。
16. 动态槽创建成功、幂等重放、重载、已有目录/任务拒绝、失败和取消回滚均通过；空槽
    在部署前不能启动，创建和代理升级均不影响既有 Minecraft PID。

不得用生产玩家服作为首次启停验收目标。

发生异常时先将 `ServerControl:Enabled` 设回 `false`。这会立即禁止新动作，不影响
已运行的 Minecraft 进程。随后停止代理计划任务并恢复安装脚本生成的配置、EXE 和
计划任务备份。代理不会强杀 Java，也不会在失败后自动重启已停止的冲突服；这种
失败关闭行为用于防止两个冲突服务同时运行。

`0.4.0` 引入新的数据库动作值。回滚到不认识 `DeleteServerFiles` 的 API 版本前，必须确认
生产尚未产生任何删除操作记录；一旦已有记录，应回滚到仍包含该合同的兼容构建，而不是
直接切回 `0.27.3`。代理可以独立回滚，但回滚后 API 会因心跳能力关闭而自动隐藏删除按钮。

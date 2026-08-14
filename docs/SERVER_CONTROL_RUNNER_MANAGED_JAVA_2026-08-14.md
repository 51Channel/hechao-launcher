# owl5 活动槽受管 Java 修复（2026-08-14）

## 1. 故障

owl5 重启后，管理员在服控面板启动“赫朝商务追杀”。目录目标实际为统一活动槽
`activity`，命令 `6b5160aa-df34-4781-8d25-0863c10fbb1d` 于本地时间
`2026-08-14 17:43` 返回 `START_TIMEOUT`。计划任务本身返回成功，但 `25568` 没有
开始监听。

活动槽在 `2026-08-14 17:16` 部署了档案
`hechao-business-manhunt-paper-1.21.11 / 1.0.0`。该包的 `start.bat` 使用相对命令
`java`，而计划任务继承的机器 `PATH` 不包含 Java；控制台日志确认批处理找不到
`java`，进程随即退出。后台等待端口超时只是后续表现。

## 2. 修复

源码提交 `5476f394e8468a9757fc27cf42b1e409a9c62c49` 为受管 runner 增加
`HECHAO_JAVA_HOME`：

- 优先读取进程值，再读取机器值；
- 配置后必须存在 `bin\java.exe`，否则在执行服务端批处理前失败；
- 只为该次受管启动设置 `JAVA_HOME`，并把 Java `bin` 放到 `PATH` 首位；
- 未配置时保持原有启动行为。

PowerShell 受控启动探针通过 `6/6`，ServerControlAgent Release 测试通过 `58/58`，
两个脚本均通过 PowerShell 7 解析和 `git diff --check`。

## 3. 生产部署

第一次尝试原位替换公共 `Run-MinecraftServer.ps1` 时，正在承载内部大厅的旧 runner
持有文件，Windows 拒绝替换。部署脚本自动恢复变量和暂存文件；原 runner 哈希、
活动任务、端口和大厅 Java PID 均未变化。

最终采用并行版本化部署，只切换活动槽计划任务：

- runner：`C:\ProgramData\Hechao\ServerControl\Run-MinecraftServer.5476f39.ps1`；
- SHA-256：`ADCEC91F97B6BD6A77C7698CB98D3521D721646AF454D24DB6DF4614FCDBBD89`；
- 机器 `HECHAO_JAVA_HOME`：`E:\jdk`；
- 任务：`Hechao-Server-ActivityNeoForge`；
- 回滚备份：`E:\manual-backups\activity-managed-java-20260814T100255Z`。

部署时先临时禁用活动任务，运行只执行 `java -version` 的临时受管批处理，再替换任务
动作并恢复任务。探针退出码为 `0`，识别 OpenJDK 21。最终活动任务为 `Ready`，
`25568` 无监听，服控代理保持单实例；内部大厅 Java PID `7328` 和启动时间未变化。
本次没有启动、停止或重启任何 Minecraft 服务端。

## 4. 回滚

先确认活动任务停止且 `25568` 无监听，然后从备份目录导入
`Hechao-Server-ActivityNeoForge.xml`，恢复原任务动作。再把机器
`HECHAO_JAVA_HOME` 恢复为备份状态，并删除版本化 runner。回滚不需要停止内部大厅，
但必须再次核对 Java PID、活动任务状态和端口。

## 5. 未完成验收

无 Minecraft 探针已经证明计划任务可以找到 Java，但不替代真实活动服启动、Paper
插件加载、Velocity 转发和真人进服验收。活动槽保持停止，下一次管理员明确启动后需
检查 `logs/latest.log`、`25568`、部署身份和服控操作结果。

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

## 4. 真实开服恢复

Java 修复后的第一次真实启动不再出现“找不到 java”，但 Paperclip 停在
`Downloading mojang_1.21.11.jar`。owl5 到 Mojang 元数据和 Microsoft CDN 的请求长时间
没有进度，服控在三分钟后返回 `START_TIMEOUT`，当时受管 Java 仍在等待下载。通过既有
控制台桥发送 `Ctrl+C` 并停止包装任务后，清理运行标记；没有强制结束其他 Java 进程。

使用另一网络从 Mojang 官方版本清单下载并核验服务端核心，再原子安装到
`E:\ActivityNeoForge\cache\mojang_1.21.11.jar`：

- 大小：`56,327,581` 字节；
- SHA-1：`64bb6d763bed0a9f1d632ec347938594144943ed`；
- SHA-256：`F83B8E093865806F931C7E34AAE41B177D4C076335263DD124C75D6D65DD1726`；
- 残缺缓存备份：`E:\manual-backups\activity-mojang-cache-20260814T102247Z`。

随后活动服在 `19.507s` 内首次完成 Paper `1.21.11-132` 启动，商务追杀插件输出
`Hechao Business Manhunt is ready. OPs use /admingame to start.`。该次日志同时确认
`config\paper-global.yml` 尚未启用 Velocity modern forwarding；服务端经
`save-all flush` 和 `stop` 正常保存并退出。

停服状态下从活动目录现有 `forwarding.secret` 读取密钥，并与生产
`E:\Velocity\forwarding.secret` 在 VPS 内做固定时间等值校验。密钥未输出、未进入命令
参数、文档或 Git。随后只修改 `paper-global.yml` 的 `proxies.velocity.enabled` 和
`secret`，原始配置保存在
`E:\manual-backups\activity-paper-velocity-20260814T103110Z`。Paper 启动后规范化的配置
SHA-256 为
`6C99D9D6DB1D2852CE28C59FFBB9D50F372A3E88E3CB388A4C8C13A23C83521C`。

`2026-08-14 18:39 +08:00` 的最终生产快照：

- 商务追杀任务为 `Running`，PID `3652` 使用 `E:\jdk\bin\java.exe`，唯一监听
  `127.0.0.1:25568`；
- 当前日志包含 `Done` 和商务追杀插件 ready，不再包含 Velocity secret 错误；
- Velocity 任务在 VPS 重启后原为停止，已恢复为 `Running`，PID `3020` 使用独立
  Temurin Java 25，唯一监听 `0.0.0.0:25577`；
- Velocity 日志包含 `Done` 且 ERROR 为 `0`，`activity` 仍唯一指向
  `127.0.0.1:25568`；
- 服控代理任务保持 `Running`，活动运行标记已重新生成；两个监听 PID 在额外十秒稳定
  窗口内未变化。

活动日志还有 Emotecraft 的三行代理消息上限提示，属于同一提示横幅，不是启动失败；
其余 ERROR 为 `0`。结构化结果见
[`BUSINESS_MANHUNT_STARTUP_RECOVERY_2026-08-14.json`](evidence/BUSINESS_MANHUNT_STARTUP_RECOVERY_2026-08-14.json)。

## 5. 回滚

先确认活动任务停止且 `25568` 无监听，然后从备份目录导入
`Hechao-Server-ActivityNeoForge.xml`，恢复原任务动作。再把机器
`HECHAO_JAVA_HOME` 恢复为备份状态，并删除版本化 runner。回滚不需要停止内部大厅，
但必须再次核对 Java PID、活动任务状态和端口。

Paper 转发配置需要回滚时，先从统一入口阻止新连接并确认活动任务停止、`25568` 无监听，
再从 `activity-paper-velocity-20260814T103110Z` 恢复原文件。Mojang 核心缓存需要回滚时
只能在活动任务停止后移走当前官方核心；不要恢复残缺缓存作为可启动制品。

## 6. 未完成验收

计划任务、Paper、商务追杀插件、Velocity 配置、两个监听和服控运行标记已经真实验收。
仍需使用正版玩家账号从赫朝启动器取得 fresh grant，经统一 Velocity 入口进入商务追杀
世界；该真人验收不能由端口和日志替代。若本活动会使用大体积 Emotecraft 动作，还需在
单独维护窗口验证并设置 Velocity 的插件消息上限。

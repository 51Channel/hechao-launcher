# 启动器 0.12.3 正式发布记录

> 状态：已发布到私有 OSS；自动测试、覆盖安装与 Activity 安装版真实进服验收通过
>
> 正式标签：`launcher-v0.12.3`
>
> 制品源码提交：`e6a160c46b89e2d5e607363e662b327760930324`
>
> 直接回滚版本：`launcher-v0.12.2`

## 1. 修复内容

`0.12.2` 已把五个 JVM 原生目录属性统一到不含 U+200C 的目录联接，但 Windows
原生加载器仍可能解析到联接背后的真实数据目录。Activity NeoForge 因此会偶发在
模组发现阶段停止，并报告：

```text
java.lang.UnsatisfiedLinkError: lwjgl.dll: Can't find dependent libraries
```

`0.12.3` 不再把目录联接作为 LWJGL 原生库最终运行位置。每次启动前，启动器会在
`%LocalAppData%\Hechao\Launcher\native-runs` 下创建真实物理目录，并完成：

- 清理同档案的过期运行目录；目录仍被占用时改用独立恢复目录。
- 对旧格式已解压原生库逐文件复制并复验 SHA-256。
- 对现代 LWJGL 允许从签名清单中的原生 JAR 在安全目录内运行时解压。
- 验证目录可写，存在旧格式 `lwjgl.dll` 时先执行 Windows 加载预检。
- 失败时清理临时目录和半成品目录，在 Java 启动前关闭失败。
- 将 `java.library.path`、`org.lwjgl.librarypath`、`jna.tmpdir`、
  `org.lwjgl.system.SharedLibraryExtractPath` 和 `io.netty.native.workdir`
  唯一指向该物理目录。

玩家数据、每档案 `.minecraft`、受管 Java、设置与登录会话均不迁移。启动器仍可
兼容原数据根目录中的 U+200C，但原生 DLL 不再从该目录加载。

## 2. 制品

| 制品 | 大小 | SHA-256 | 签名 |
| --- | ---: | --- | --- |
| `Hechao-Launcher-Setup-0.12.3-win-x64.exe` | `61,874,260` | `18E786560AF14C246EFF84638BABBE8E1CC02CBFB1E1065AD9501468C20603C6` | `NotSigned` |
| 安装后的 `Hechao.Launcher.exe` | `68,793,918` | `18CF9772099EA1CA6FFEC7B8588CFFBFC3137FDACCD733291CC1242423518E07` | `NotSigned` |

安装包生产对象为
`releases/launcher/0.12.3/Hechao-Launcher-Setup-0.12.3-win-x64.exe`。私有签名下载
URL 只保存在受 ACL 保护的本机结果文件中，不进入 Git、文档或终端摘要。

## 3. 验收

- 完整 .NET 解决方案 `392/392` 通过；原生目录与进程构建专项测试 `18/18`
  通过，格式检查与 `git diff --check` 通过。
- 使用含 U+200C 的现有 Activity 数据根目录执行源码运行时冒烟；NeoForge 越过
  模组发现，Meccha 开始加载，五个原生属性均指向真实 `native-runs` 目录，收口后
  没有遗留 Java 进程。
- `0.12.2 -> 0.12.3` 静默覆盖安装成功，注册表与程序版本为 `0.12.3`，设置和
  DPAPI 登录会话哈希保持一致。
- 安装版使用真实管理员正版会话启动
  `activity-neoforge-1.21.11`，NeoForge、LWJGL、Meccha、Sodium、WorldEdit 与
  语音组件完成加载。
- 进程 `3068` 从 `2026-07-30T07:30:29.394684+08:00` 运行到
  `2026-07-30T07:34:50.474935+08:00`，连接 `mc.hehe11.fun:25565`、进入活动地图
  并以退出码 `0` 正常结束。
- `latest.log` 有 `Stopping!`，没有 `UnsatisfiedLinkError`、
  `Can't find dependent libraries` 或 `Fatal Startup Error`。
- 游戏退出后 Java 进程数为 `0`，`running-game.json` 已清理，启动器继续运行。
- 私有 OSS 匿名访问两次均为 `403`；签名回读两次均为 `200`，耗时分别为
  `1.22` 秒和 `1.06` 秒，长度与 SHA-256 一致。
- 第二次发布识别到同键同内容并校验后跳过，没有覆盖不可变对象。

结构化证据见
[`evidence/ACTIVITY_NEOFORGE_NATIVE_RUN_DIRECTORY_2026-07-30.json`](evidence/ACTIVITY_NEOFORGE_NATIVE_RUN_DIRECTORY_2026-07-30.json)。

## 4. 仍需真实玩家完成

- Member、Participant、Collaborator、Administrator 四级正版账号授权。
- 离线目标和无权限真实账号的失败关闭验收。
- 单服 Allow、Deny、维护、过期授权、重复授权和未知目标的完整行为矩阵。
- `2/3/5/20` 人逐级灰度与 TPS、MSPT、GC、API 延迟和告警容量验收。
- 灰度完成后再决定 Authorizer 从 `monitor` 切换到 `enforce`，随后启用目录强制登录。

大厅继续作为 LuckPerms、指标、告警和备份的内部承载器，但不能出现在玩家目录，也
不能被玩家连接或作为失败回退目标。

## 5. 回滚

启动器本身可回滚到 `launcher-v0.12.2`，不会删除游戏数据、档案、受管 Java、设置
或 DPAPI 会话。由于 `0.12.2` 仍可能让 Windows 原生加载器解析到含 U+200C 的联接
目标，回滚期间 Activity NeoForge 必须停止开放；API、Velocity Authorizer、
Lobby Guard 和 Minecraft 服务端不随本次启动器回滚变化。

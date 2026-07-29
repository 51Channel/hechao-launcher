# 启动器 0.12.2 正式发布记录

> 状态：已发布到私有 OSS；自动测试、安装版真实 Activity 进服与正常退出验收通过
>
> 正式标签：`launcher-v0.12.2`
>
> 制品源码提交：`6405ac760fba9422d3f82fb6d3b9111e79ee700f`
>
> 直接回滚版本：`launcher-v0.12.1`

## 1. 修复内容

`0.12.1` 已为含 U+200C 格式字符的数据根目录建立安全原生库映射，但 NeoForge
生成的启动参数仍可能带有一个后写入的 `org.lwjgl.librarypath`。JVM 因此会重新
选择原始版本目录，并在加载 `lwjgl.dll` 时报告 `Can't find dependent libraries`。

`0.12.2` 在启动进程的最后阶段统一规范五个原生目录属性：

- `java.library.path`
- `org.lwjgl.librarypath`
- `jna.tmpdir`
- `org.lwjgl.system.SharedLibraryExtractPath`
- `io.netty.native.workdir`

启动器会移除已有的重复、缺失或冲突参数，再插入唯一的安全绝对路径。无论参数来自
逐项参数列表还是打包后的引号字符串，启动前都会再次验证五个属性各出现一次、指向
同一目录且不含 Unicode 格式字符。验证失败时不会启动 Java。

本版同时让退出记录保存独立的进程号，避免游戏已经退出后再次读取失效的
`Process.Id`，确保过期的 `running-game.json` 能在启动器重启时自动清理。

## 2. 制品

| 制品 | 大小 | SHA-256 | 签名 |
| --- | ---: | --- | --- |
| `Hechao-Launcher-Setup-0.12.2-win-x64.exe` | `61,876,002` | `FEE5A53FF9A6033E96E2150E8A31D474B559581BEED14B65F939743A83C4BDCB` | `NotSigned` |
| 安装后的 `Hechao.Launcher.exe` | `68,785,734` | `EF8817CB19AC6A51C09CBEDD8685151044C7864B76F4C90293804E286923FEF3` | `NotSigned` |

安装包生产对象为
`releases/launcher/0.12.2/Hechao-Launcher-Setup-0.12.2-win-x64.exe`。私有签名下载
URL 只保存在受 ACL 保护的本机结果文件中，不进入 Git、文档或终端摘要。

## 3. 自动与真实进程验收

- 完整 .NET 解决方案 `386/386` 通过，格式检查与 `git diff --check` 通过。
- 原生路径专项测试覆盖缺失、重复、冲突、打包引号参数和真实 Activity 进程构建。
- 源码运行时冒烟测试越过 NeoForge 模组发现和 Meccha 加载，并在收口后确认没有
  遗留 Java 进程。
- `0.12.1 -> 0.12.2` 覆盖安装后，设置与登录会话保留，安装程序版本和哈希正确。
- 安装版 `0.12.2` 由真实“进入服务器”操作启动 Activity；进程中的五个原生目录
  属性全部指向
  `C:\Users\Administrator\AppData\Local\Hechao\Launcher\runtime-links\activity-neoforge-12111-natives-2CD75C95D800CB72`，
  且没有 U+200C。
- NeoForge、LWJGL、Meccha 和全部模组加载完成，客户端连接
  `mc.hehe11.fun:25565` 并进入 Activity 世界。
- 会话进程号为 `37868`，从 `2026-07-30T03:58:16.9763773+08:00` 运行至
  `2026-07-30T03:59:52.6864828+08:00`，退出码为 `0`。
- `latest.log` 出现 `Stopping!`，没有 `UnsatisfiedLinkError`、
  `Can't find dependent libraries`、`Fatal Startup Error` 或
  `FileAlreadyExistsException`。
- 游戏退出后 Java 进程数为 `0`，`running-game.json` 已清理，启动器继续正常运行。
- 私有 OSS 匿名访问两轮均为 `403`；签名回读两轮均为 `200`，耗时分别为 `1.35`
  秒和 `1.29` 秒，长度与 SHA-256 均一致。
- 第二次发布识别到同键同内容并校验后跳过，没有覆盖不可变生产对象。
- 使用进程级不可达 API 地址完成故障关闭演练：启动器显示访客、没有进服动作，不创建
  Java 进程或运行状态；恢复正常 API 后原会话自动恢复，重新授权并再次直达 Activity
  世界，退出码为 `0`。

结构化证据见
[`evidence/ACTIVITY_NEOFORGE_NATIVE_PATH_RECOVERY_2026-07-30.json`](evidence/ACTIVITY_NEOFORGE_NATIVE_PATH_RECOVERY_2026-07-30.json)
和
[`evidence/LAUNCHER_API_FAILURE_RECOVERY_2026-07-30.json`](evidence/LAUNCHER_API_FAILURE_RECOVERY_2026-07-30.json)。

## 4. 仍需真实玩家完成

- Member、Participant、Collaborator、Administrator 四级正版账号授权。
- 离线目标和无权限真实账号的失败关闭验收。
- 单服 Allow、Deny、维护、过期授权、重复授权和未知目标的完整行为矩阵。
- `2/3/5/20` 人逐级灰度与 TPS、MSPT、GC、API 延迟和告警容量验收。
- 灰度完成后再决定 Authorizer 从 `monitor` 切换到 `enforce`，随后启用目录强制登录。

大厅继续作为 LuckPerms、指标、告警和备份的内部承载器，但不能出现在玩家目录，也
不能被玩家连接或作为失败回退目标。

## 5. 回滚

直接回滚到 `launcher-v0.12.1`，只替换启动器程序，不删除游戏数据、档案、受管
Java、设置或 DPAPI 会话。`0.12.1` 不包含本次 `org.lwjgl.librarypath` 最终规范化
与退出进程号修复，因此 Activity 应在回滚期间停止开放。API、Velocity Authorizer、
Lobby Guard 和 Minecraft 服务端不随本次启动器回滚变化。

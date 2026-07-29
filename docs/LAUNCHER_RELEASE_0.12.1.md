# 启动器 0.12.1 正式发布记录

> 状态：已发布到私有 OSS；自动发布、Activity 冷/热启动及安装版真实进服验收通过
>
> 正式标签：`launcher-v0.12.1`
>
> 制品源码提交：`51a82cf5e3b30e62db37db1bf6911e9a661eb818`
>
> 直接回滚版本：`launcher-v0.12.0`

## 1. 修复内容

既有游戏数据根目录 `H:\hechao ‌Launcher` 的空格后包含 U+200C 格式字符。Windows
与 Java 对该路径的三类操作行为不同：

- NeoForge Jar-in-Jar 使用 `CREATE_NEW` 创建临时文件时，整目录 junction 会返回文件
  已存在，导致模组发现阶段停滞。
- Java 运行时直接位于该路径时，JVM 原生依赖解析会失败。
- LWJGL `natives` 使用 NTFS 短路径时，JVM 会重新解析成长路径，导致
  `lwjgl.dll` 报 `Can't find dependent libraries`。

`0.12.1` 将三类路径分别处理：

- 游戏工作目录和游戏参数使用真实 NTFS 8.3 短路径，保留 `CREATE_NEW` 语义。
- 受管 Java 使用只指向运行时目录的安全 junction。
- 当前版本 `natives` 使用独立安全 junction，并且只改写
  `java.library.path`、`jna.tmpdir`、LWJGL 解压目录和 Netty 工作目录。

玩家的模组、配置、日志、存档和版本文件仍保留在原档案目录，没有复制或迁移整套
客户端。

## 2. 制品

| 制品 | 大小 | SHA-256 | 签名 |
| --- | ---: | --- | --- |
| `Hechao-Launcher-Setup-0.12.1-win-x64.exe` | `61,874,773` | `6C5783AD9F0B21F0E7DB6BB4F9FC6E7A62BEFF3B550169F48D4F9CF8DBF1B907` | `NotSigned` |
| 安装后的 `Hechao.Launcher.exe` | `68,783,690` | `86FDE25FC5C6A929C649FC53C599B9563A56AF2C64FCB052631D09DAB8C7FDEE` | `NotSigned` |

安装包生产对象为
`releases/launcher/0.12.1/Hechao-Launcher-Setup-0.12.1-win-x64.exe`。私有签名下载
URL 只保存在受 ACL 保护的本机结果文件中，不进入 Git、文档或终端摘要。

## 3. 自动与真实进程验收

- 完整 .NET 解决方案 `384/384` 通过，格式检查与 `git diff --check` 通过。
- Activity NeoForge `21.11.42` 冷启动和热启动各完成一轮真实 Java 进程验收。
- 两轮均越过 Jar-in-Jar 模组发现并进入 `[Meccha Chameleon] Loading`。
- `jcmd VM.system_properties` 确认 `user.dir` 为无 U+200C 的真实短路径，四个原生目录
  属性均为独立安全映射。
- 两轮结束后均自动终止测试客户端，没有遗留 Activity Java 进程。
- 已安装的 `0.12.1` 由真实“进入服务器”操作启动 Activity，完成 Microsoft/Minecraft
  会话、NeoForge 与全部模组加载、连接 `mc.hehe11.fun` 并进入世界。
- 该安装版会话随后正常退出，退出码为 `0`，日志出现 `Stopping!`，没有遗留 Java
  进程；未出现 `UnsatisfiedLinkError`、`FileAlreadyExistsException` 或致命启动错误。
- `0.12.0 -> 0.12.1` 覆盖升级、隔离干净安装、双轮卸载、设置和 DPAPI 会话保留
  均通过。
- 私有 OSS 对象匿名访问返回 `403`；两次签名回读返回 `200`，耗时分别为 `1.28`
  秒和 `1.37` 秒，长度与 SHA-256 均一致。
- 第二次发布识别到同键同内容并校验后跳过，没有覆盖不可变生产对象。

## 4. 仍需真实玩家完成

- 普通、Participant、Collaborator、Administrator 四级正版账号授权。
- 同档案和跨档案各至少三轮切换，并核对每轮仅有一个 Minecraft 进程。
- 使用真实 Activity 账号完成断线重连和失败恢复。
- 启动器关闭后重新打开，接管现有游戏并切换到另一档案。
- `2/3/5/20` 人逐级灰度与 TPS、MSPT、GC 容量验收。

这些外部门槛不会改变大厅永久不可进入的安全边界。

## 5. 回滚

回滚到 `launcher-v0.12.0` 只替换启动器程序，不删除游戏数据、档案、受管 Java、
设置或 DPAPI 会话。`0.12.0` 不包含本次特殊路径修复，因此只应在 Activity
停止开放并确认数据根目录不含格式字符时使用。API、Velocity Authorizer 和 Lobby
Guard 不随本次启动器回滚变化。

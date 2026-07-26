# 赫朝启动器 0.11.9 发布记录

> 构建日期：`2026-07-26`
>
> 启动器源码提交：`1699de93dbf1fd95dc800f28eb5a735c277ea930`
>
> Git 标签：`launcher-v0.11.9`
>
> 配套 API：`0.12.0-20260725T203001Z`，本次没有服务端变更
>
> 状态：私有 OSS 灰度候选已验证；替换 `0.11.8`

## 根因

玩家点击进入服务器后，Minecraft 进程约 `0.1` 秒以代码 `1` 退出，游戏目录没有
生成新日志。捕获 Java 标准错误后确认：

```text
ClassNotFoundException: net.fabricmc.loader.impl.launch.knot.KnotClient
```

`0.11.8` 已为每档案 Java 运行时创建安全目录联接，因此 JVM 本身可以启动；但
Fabric Loader、Intermediary 和版本 JAR 的类路径仍由含不可见 U+200C 格式字符的
原 `.minecraft` 路径生成。Java 无法从该类路径加载主类，于是在 Minecraft 日志
系统初始化前退出。

## 修复

- 在读取并验证物理档案后，为 `.minecraft` 单独创建受校验的安全目录联接。
- 使用安全游戏目录构建 `MinecraftPath`，从而覆盖类路径、游戏目录和工作目录。
- Java 运行时继续使用独立安全联接；物理运行时仍位于档案目录。
- 启动前的物理档案完整性与重解析点检查保持不变。
- 新增显式真实 Java 启动冒烟；使用假会话运行 15 秒后主动结束，并检查标准错误
  不含主类或 `ClassNotFoundException`。
- 原构建冒烟新增断言，Java 可执行路径和全部启动参数均不得含 U+200C。

## 验收证据

- Debug 与 Release 完整解决方案测试均为 `218/218`。
- 修复前真实启动冒烟稳定复现 `KnotClient` 类缺失。
- 修复后同一档案、同一数据根目录和同一 Java 21 持续运行超过 15 秒并进入
  Minecraft 初始化；测试随后主动结束假会话进程。
- 安全别名分别解析到
  `instances\base-1.21.11\runtime` 与
  `instances\base-1.21.11\.minecraft`。
- 本机从 `0.11.8` 静默覆盖到 `0.11.9`，设置和 DPAPI 赫朝会话 SHA-256
  安装前后完全一致。
- 本次没有修改客户端签名档案、API、数据库、Velocity 或 Minecraft 服务端。

## 制品

| 项目 | 值 |
| --- | --- |
| 启动器 EXE | `D:\Hechao Launcher\Hechao.Launcher.exe` |
| EXE ProductVersion | `0.11.9+1699de93dbf1fd95dc800f28eb5a735c277ea930` |
| EXE 大小 | `68,679,470` 字节 |
| EXE SHA-256 | `1A222D5E80AE63EC2D9C29DA5B34AEA4FDC576FAF8DE297EEAEE1A744843D4CB` |
| 安装包 | `artifacts/installer/Hechao-Launcher-Setup-0.11.9-win-x64.exe` |
| 安装包大小 | `61,815,081` 字节 |
| 安装包 SHA-256 | `C80782CD522EFBCC1E0834AEE46583E8C0355B8B787B7174CAAF5C808CA19469` |
| Windows 签名 | EXE 与安装包均为 `NotSigned` |

## 私有灰度发布

安装包已写入不可变对象：

```text
releases/launcher/0.11.9/Hechao-Launcher-Setup-0.11.9-win-x64.exe
```

远端元数据、第二次跳过校验、匿名 `403`、24 小时签名下载 `200`、长度和 SHA-256
均已复验。OSS 原始节点完整下载耗时约 `1.43` 秒；短时链接只保存在本机 ACL
保护目录。`0.11.8` 对象保留用于审计和回滚分析，但不再提供给灰度玩家。

## 回滚

本版本修复的是 `0.11.8` 的阻断问题，不应回滚到 `0.11.8`。若发现新的独立阻断，
可关闭启动器后使用 `0.11.7` 覆盖安装；设置、会话和游戏数据不在程序覆盖边界内。
回滚到 `0.11.7` 会失去每档案 Java 和自定义 Java 功能。

## 边界

本次只修改 Windows 启动器的安全游戏路径、测试、版本和文档。没有公开下载目录、
永久链接、API 部署、数据库迁移、Velocity 重启或 Minecraft 服务启停。

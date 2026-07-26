# 赫朝启动器 0.11.8 发布记录

> 构建日期：`2026-07-26`
>
> 启动器源码提交：`71f2a479c4bdf755dbdfa151f95106384c3c4ea4`
>
> Git 标签：`launcher-v0.11.8`
>
> 配套 API：`0.12.0-20260725T203001Z`，本次没有服务端变更
>
> 状态：已被 `0.11.9` 替代，停止分发；未面向玩家公开发布

## 后续发现

`0.11.8` 只把 Java 可执行文件切换到安全目录联接，Fabric 类路径和游戏目录仍引用
含不可见格式字符的原始路径。结果是 JVM 可以启动，但会立即报
`ClassNotFoundException: net.fabricmc.loader.impl.launch.knot.KnotClient`。
该候选不得继续灰度；修复与重新验收记录见
[`LAUNCHER_RELEASE_0.11.9.md`](LAUNCHER_RELEASE_0.11.9.md)。

## 功能范围

`0.11.8` 把 Java 运行时纳入每个客户端档案：

- 安装或修复客户端时自动安装档案声明的 Java。
- Java 保存在 `instances\<profile-id>\runtime`，与该档案的 `.minecraft` 同级。
- 基础、活动等 1.21.11 档案使用 Java 21；PVP 1.20.1 档案使用 Java 17。
- `shared\runtime` 只作为旧版本迁移种子；验证主版本一致后复制复用，避免重新下载。
- Java 安装状态写入档案根目录 `.hechao-java.json`，只有客户端和 Java 都完成才显示就绪。
- 客户端文件占总进度 `0%` 至 `85%`，Java 准备占 `85%` 至 `100%`。

运行配置新增每档案 Java 选择：

- 默认“自动”使用启动器随客户端安装的 Java。
- “自定义”允许玩家选择 `java.exe` 或 `javaw.exe`。
- 保存前执行 `java -version` 并校验档案所需主版本；错误版本不会写入设置。
- 自定义路径按客户端档案 ID 保存，不会影响其他档案。
- 恢复“自动”只移除覆盖设置，不删除玩家自己的 Java。

## 特殊路径兼容

旧数据根目录可能含有不可见 Unicode 格式字符。Java 在这类路径下会错误定位
`java.dll`，而 8.3 短文件名并不保证可用。`0.11.8` 会在
`%LocalAppData%\Hechao\Launcher\runtime-links` 创建指向对应档案运行时的本地目录联接，
并从不含格式字符的别名启动 Java。联接必须解析到预期目录，否则启动停止；游戏数据、
对象缓存、运行时和玩家设置不会被迁移或改名。

## 验收证据

- Debug 和 Release 完整解决方案测试均为 `217/217`。
- 新增 Java 版本解析、Minecraft 版本映射、每档案状态、旧运行时复用、设置持久化和
  Windows 目录联接测试。
- 使用真实基础档案把 Java 21 从旧共享目录迁移到
  `instances\base-1.21.11\runtime`，写入状态文件并成功构建 Minecraft 进程；
  测试没有启动 Minecraft。
- 兼容别名中的 `java -version` 返回 OpenJDK 21，进程可执行路径不含不可见格式字符。
- 正式安装版界面确认基础档案显示 Java 21、PVP 1.20.1 显示 Java 17，
  自动与自定义控制、路径、内存和修复按钮均完整可见。
- 本机从 `0.11.7` 静默覆盖到 `0.11.8`，安装前后 `settings.json` 和 DPAPI
  赫朝会话 SHA-256 一致。
- 本次没有修改 API、数据库、Velocity、客户端签名档案或 Minecraft 服务端。

## 制品

| 项目 | 值 |
| --- | --- |
| 启动器 EXE | `D:\Hechao Launcher\Hechao.Launcher.exe` |
| EXE ProductVersion | `0.11.8+71f2a479c4bdf755dbdfa151f95106384c3c4ea4` |
| EXE 大小 | `68,679,459` 字节 |
| EXE SHA-256 | `70F3177BE21933AF54121144403AA3B8CCC55E5D0C86BED9D18B1DBB8EDDF806` |
| 安装包 | `artifacts/installer/Hechao-Launcher-Setup-0.11.8-win-x64.exe` |
| 安装包大小 | `61,816,090` 字节 |
| 安装包 SHA-256 | `778F9F69439386E60CF5BEFA25BF7448EE4EAF92A05447EBB0A97553F5047BC0` |
| Windows 签名 | EXE 与安装包均为 `NotSigned` |

## 私有灰度发布

安装包已写入不可变对象：

```text
releases/launcher/0.11.8/Hechao-Launcher-Setup-0.11.8-win-x64.exe
```

远端元数据、第二次跳过校验、匿名 `403`、24 小时签名下载 `200`、长度和 SHA-256
均已复验。OSS 原始节点完整下载耗时约 `1.42` 秒；临时签名地址只保存在本机
ACL 保护目录，不进入 Git、文档或长期公告。

## 回滚

若灰度发现阻断问题，关闭 `0.11.8`，使用
`Hechao-Launcher-Setup-0.11.7-win-x64.exe` 覆盖安装。启动器设置、DPAPI 会话、
客户端档案和每档案运行时都位于程序覆盖边界之外。旧版会继续从
`shared\runtime` 启动 Java，不读取 `.hechao-java.json` 或每档案自定义 Java 设置。

## 边界

本次只修改 Windows 启动器、存储说明、测试和发布文档。发布动作只新增私有 OSS
安装包对象和短时内部链接，没有公开目录、永久玩家链接、API 部署、数据库迁移、
Velocity 重启或 Minecraft 服务启停。

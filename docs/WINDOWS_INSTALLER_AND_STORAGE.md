# Windows 安装包与游戏数据目录

> 启动器源码版本：`0.11.6`
> 存储结构版本：`2`
> 更新日期：`2026-07-25`

## 1. 已确定的产品形态

赫朝启动器采用正式安装式客户端，而不是把 EXE 放在哪里就在哪里生成 `.minecraft`：

- 启动器程序安装到 `%LocalAppData%\Programs\Hechao Launcher`。
- 默认游戏数据根目录为 `%LocalAppData%\Hechao\GameData`。
- 每个客户端档案拥有独立的 `instances\<profile-id>\.minecraft`。
- 不同 Minecraft、Fabric、Forge、NeoForge 和原版档案不能共用可写游戏目录。
- `assets`、`libraries`、下载对象和 Java 运行时在数据根目录下共享，避免重复占用。
- 设置保存在 `%LocalAppData%\Hechao\Launcher\settings.json`，不放在程序目录。
- 更新或卸载启动器不会删除游戏数据、档案、存档和共享下载。

这种结构同时满足“像正式游戏客户端一样安装”和“不同活动客户端互相隔离”两个要求。PCL 式当前目录 `.minecraft` 只适合便携使用，不作为玩家默认模式。

## 2. 目录结构

```text
%LocalAppData%\Programs\Hechao Launcher\
  Hechao.Launcher.exe
  Assets\

%LocalAppData%\Hechao\Launcher\
  settings.json
  game-exits.json
  diagnostics\

%LocalAppData%\Hechao\GameData\
  instances\
    base-1.21.11\
      .hechao-install.json
      .minecraft\
        versions\
        mods\
        config\
        saves\
        resourcepacks\
    activity-neoforge-1.21.11\
      .hechao-install.json
      .minecraft\
    .base-1.21.11.previous\
      .hechao-install.json
      .minecraft\
  shared\
    objects\
    runtime\
  .hechao\
    locks\
    storage-layout.json
```

职责边界：

| 目录 | 用途 | 能否随卸载删除 |
| --- | --- | --- |
| 程序目录 | 启动器 EXE、图标授权文件 | 可以 |
| `Launcher` | 启动器设置、本机会话、退出记录和玩家生成的诊断包 | 默认保留 |
| `instances` | 每个档案独立的 `.minecraft` | 默认保留 |
| `shared/objects` | SHA-256 内容寻址下载缓存 | 默认保留 |
| `shared/runtime` | 档案共用的受管 Java | 默认保留 |
| `.hechao/locks` | 跨进程安装锁 | 可自动重建 |

## 3. 档案安装与更新

每个服务器目录记录一个 `clientProfileId`。启动器只把同一档案 ID 的文件安装到对应 `.minecraft`，不会把活动服模组混入大厅或生存服。

更新流程：

1. 验证 ECDSA 签名清单和所有受管路径。
2. 在 `instances` 下创建同档案暂存目录。
3. 保留 `saves`、`screenshots`、`resourcepacks`、`shaderpacks`、日志、崩溃报告和常用选项文件。
4. 对照 SHA-256 复用已存在文件或共享对象，缺失文件使用断点续传下载。
5. `assets` 与 `libraries` 优先通过 NTFS 硬链接复用；不支持时退回普通复制。
6. 写入结构版本为 `2` 的 `.hechao-install.json`。
7. 原子切换活动目录，并保留一个 `.<profile-id>.previous` 回滚版本。

从 `0.11.1` 起，清单校验、文件保留、哈希检查和目录切换在界面线程之外执行；进度值只从安装器单向更新界面。安装入口具有重复点击保护和异常边界，任何未预期错误都会结束当前任务、恢复按钮，并保持原活动版本不被替换。

模组、加载器和受管配置以签名清单为准。未列入新清单的旧受管文件不会进入新活动版本；玩家存档等可写数据不依赖清单存在。

## 4. 旧版目录迁移

启动器 `0.9.0` 及后续版本首次读取旧设置时自动执行一次迁移：

| 旧位置 | 新位置 |
| --- | --- |
| `%AppData%\Hechao\instances\<profile-id>` | `%LocalAppData%\Hechao\GameData\instances\<profile-id>\.minecraft` |
| 自定义旧客户端根目录 | 原自定义根目录下的 `instances\<profile-id>\.minecraft` |
| `.hechao/cache/objects` | `shared/objects` |
| 旧 `.hechao-install.json` | 保留在档案根目录并升级为结构版本 `2` |

迁移安全规则：

- 只识别含安装状态、`hechao-profile.json` 或 `versions` 的档案目录。
- 自定义根目录里的无关文件夹不会被移动。
- 遇到符号链接、目录联接或其他重解析点时停止迁移。
- 同盘优先使用目录重命名；跨盘先复制到临时目录，完成后再切换。
- 迁移失败时不主动删除原目录，启动器显示错误并停止启动。
- 迁移可重复执行；完成后的再次执行不会重复嵌套 `.minecraft`。

设置页更换游戏数据目录只切换后续使用的根目录，不会在界面线程里搬运数百 MB 或数 GB 的现有档案。旧目录保持不变；需要整体迁盘时先退出启动器，再按本手册备份并迁移，或重新选择原目录恢复。

`0.11.2` 启动 Java 前会把受管 `java.exe` 转换为 Windows 提供的兼容短路径。这样即使玩家过去选择的目录意外含有不可见格式字符，Microsoft Java 21 仍能定位同目录的 `java.dll`；游戏目录、对象缓存和玩家设置不会因此迁移或改名。

## 5. 安装、升级与卸载

安装包使用 NSIS 3，默认按当前 Windows 用户安装，不要求管理员权限：

```powershell
.\tools\Build-WindowsInstaller.ps1
```

构建脚本会依次执行：

1. 运行完整解决方案测试。
2. 生成 `win-x64`、自包含、单文件启动器。
3. 编译简体中文/英文安装向导。
4. 输出安装包和对应 SHA-256 文件。

输出：

```text
artifacts\installer\Hechao-Launcher-Setup-<version>-win-x64.exe
artifacts\installer\Hechao-Launcher-Setup-<version>-win-x64.exe.sha256
```

本机构建依赖：

- .NET SDK `10.0.302`
- NSIS 3，可通过 `winget install --id NSIS.NSIS --exact` 安装

升级使用相同 `AppId` 和安装目录，覆盖启动器程序但保留游戏数据。卸载器只删除它登记安装的程序文件和快捷方式，不包含 `%LocalAppData%\Hechao\GameData` 或 `%LocalAppData%\Hechao\Launcher` 删除规则。

## 6. 发布验证

每个候选版本至少完成：

```powershell
dotnet test Hechao.Launcher.sln -c Release
.\tools\Build-WindowsInstaller.ps1 -SkipTests
Get-FileHash .\artifacts\installer\Hechao-Launcher-Setup-*-win-x64.exe -Algorithm SHA256
```

再在隔离目录执行安装/卸载冒烟测试，核对：

- 安装后的 `Hechao.Launcher.exe` 产品版本正确。
- 开始菜单和可选桌面快捷方式指向安装目录。
- 升级后设置与游戏数据仍存在。
- 卸载后程序目录被清理，游戏数据根目录不受影响。
- 退出记录和本地诊断包不进入安装包，也不随升级被覆盖。
- 安装包、EXE 和 SHA-256 记录进入资产清单。

当前已确认不为首版购买 Authenticode 代码签名证书，安装包和 EXE 的预期状态为 `NotSigned`。这不会影响程序运行或客户端档案的 ECDSA 验签，但会增加 SmartScreen 首次运行提示。内部灰度和正式公告必须只使用官方来源，并同时公布安装包文件名、版本、大小和 SHA-256；以后增加代码签名时应发布新版本，不得覆盖既有安装包。

`0.10.0` 候选已重新执行 `0.9.1` 原地升级、干净安装和静默卸载。安装后的 EXE 产品版本为 `0.10.0+9cba23e9d0b5ba799af50dcc2ef0018cfe5a31e4`，SHA-256 与构建原件一致；IconPark 授权文件、设置和游戏数据均保留，卸载后程序目录、开始菜单和注册表项均已清理。安装包仍未上传或向玩家分发，完整记录见 [`LAUNCHER_RELEASE_0.10.0.md`](LAUNCHER_RELEASE_0.10.0.md)。

`0.11.1` 已完成 `0.11.0` 原地升级和启动冒烟验证。安装后 EXE、注册表版本、设置、DPAPI 会话和 IconPark 授权文件均通过，完整记录见 [`LAUNCHER_RELEASE_0.11.1.md`](LAUNCHER_RELEASE_0.11.1.md)。安装包仍为本地内部候选，未上传 OSS，也没有建立公开下载地址。

`0.11.2` 已从 `0.11.1` 覆盖升级并保留设置、DPAPI 会话和已安装档案。基础客户端生产续传完成后显示 `100%` 与“客户端已就绪”；包含不可见字符的数据根目录通过 Windows 兼容短路径完成 Java 21 进程构建冒烟测试。安装包仍为本地内部候选，未上传 OSS，完整记录见 [`LAUNCHER_RELEASE_0.11.2.md`](LAUNCHER_RELEASE_0.11.2.md)。

`0.11.3` 已从 `0.11.2` 覆盖升级并保留设置及已安装档案；本机验收时没有现存 DPAPI 会话文件，因此本次不声明登录状态保留。正式候选在 1500 x 860 窗口完成下载页、服务器页和账号页边界验收；安装包仍为本地内部候选，未上传 OSS，完整记录见 [`LAUNCHER_RELEASE_0.11.3.md`](LAUNCHER_RELEASE_0.11.3.md)。

`0.11.4` 已从 `0.11.3` 覆盖升级，设置及已安装档案保持原位。正式安装版分别完成登录与注册选中状态截图，并由真实 WPF 像素测试确认右侧边框存在；安装包仍为本地内部候选，未上传 OSS，完整记录见 [`LAUNCHER_RELEASE_0.11.4.md`](LAUNCHER_RELEASE_0.11.4.md)。

`0.11.6` 已覆盖本机失败候选 `0.11.5`，设置及已安装档案保持原位。正式安装版确认登录和注册按钮等宽、四边完整，登录表单与注册表单均恢复整栏宽度；安装包仍为本地内部候选，未上传 OSS，完整记录见 [`LAUNCHER_RELEASE_0.11.6.md`](LAUNCHER_RELEASE_0.11.6.md)。

## 7. 第三方构建资产

- 安装器：[NSIS 3](https://nsis.sourceforge.io/Docs/AppendixI.html)，使用 zlib/libpng 许可并允许商业应用。
- 简体中文与英文界面使用 NSIS 自带语言资源，不额外下载第三方翻译包。

该依赖只参与安装包构建，不进入 Minecraft 客户端档案，也不接触玩家账号或服务器凭据。

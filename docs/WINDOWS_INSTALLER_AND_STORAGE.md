# Windows 安装包与游戏数据目录

> 启动器源码版本：`0.9.1`
> 存储结构版本：`2`
> 更新日期：`2026-07-24`

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

## 5. 安装、升级与卸载

安装包使用 NSIS 3，默认按当前 Windows 用户安装，不要求管理员权限：

```powershell
.\tools\Build-WindowsInstaller.ps1
```

构建脚本会依次执行：

1. 预检 Windows 代码签名证书、SignTool 和 RFC 3161 时间戳。
2. 运行完整解决方案测试。
3. 生成 `win-x64`、自包含、单文件启动器并完成 Authenticode 签名。
4. 编译简体中文/英文安装向导，并在 NSIS 编译期间签署卸载程序。
5. 签署外层安装包。
6. 验证启动器、卸载程序和安装包的发布者与时间戳。
7. 在签名完成后输出启动器和安装包的 SHA-256 文件。

输出：

```text
artifacts\publish\win-x64\Hechao.Launcher.exe
artifacts\publish\win-x64\Hechao.Launcher.exe.sha256
artifacts\installer\Hechao-Launcher-Setup-<version>-win-x64.exe
artifacts\installer\Hechao-Launcher-Setup-<version>-win-x64.exe.sha256
```

本机构建依赖：

- .NET SDK `10.0.302`
- NSIS 3，可通过 `winget install --id NSIS.NSIS --exact` 安装
- Windows SDK x64 SignTool，可通过 `.\tools\Install-WindowsSigningTools.ps1` 安装受校验的本机副本
- 公网受信任的 RSA Authenticode 代码签名证书及颁发机构时间戳地址

没有正式证书时，开发者只能使用
`.\tools\Build-WindowsInstaller.ps1 -SkipSigning` 生成不可分发的内部测试包。完整证书配置、安全边界和验签方法见 [Windows 代码签名](WINDOWS_CODE_SIGNING.md)。

升级使用相同 `AppId` 和安装目录，覆盖启动器程序但保留游戏数据。卸载器只删除它登记安装的程序文件和快捷方式，不包含 `%LocalAppData%\Hechao\GameData` 或 `%LocalAppData%\Hechao\Launcher` 删除规则。

## 6. 发布验证

每个候选版本至少完成：

```powershell
dotnet test Hechao.Launcher.sln -c Release
.\tools\Build-WindowsInstaller.ps1 -SkipTests
Get-AuthenticodeSignature .\artifacts\publish\win-x64\Hechao.Launcher.exe
Get-AuthenticodeSignature .\artifacts\installer\Hechao-Launcher-Setup-*-win-x64.exe
```

再在隔离目录执行安装/卸载冒烟测试，核对：

- 安装后的 `Hechao.Launcher.exe` 产品版本正确。
- 开始菜单和可选桌面快捷方式指向安装目录。
- 升级后设置与游戏数据仍存在。
- 卸载后程序目录被清理，游戏数据根目录不受影响。
- 退出记录和本地诊断包不进入安装包，也不随升级被覆盖。
- 启动器、卸载程序和安装包的 Authenticode 状态均为 `Valid`，且包含可信时间戳。
- 安装包、EXE、最终 SHA-256、发布者和签名状态进入资产清单。

正式发布入口默认要求 Authenticode 代码签名；缺少证书、私钥、SignTool、时间戳或任一层验签失败都会停止构建。客户端档案的 ECDSA 清单签名只能保护下载内容，不能替代 Windows 对安装包和 EXE 的代码签名。

## 7. 第三方构建资产

- 安装器：[NSIS 3](https://nsis.sourceforge.io/Docs/AppendixI.html)，使用 zlib/libpng 许可并允许商业应用。
- 简体中文与英文界面使用 NSIS 自带语言资源，不额外下载第三方翻译包。

该依赖只参与安装包构建，不进入 Minecraft 客户端档案，也不接触玩家账号或服务器凭据。

# 赫朝整合包部署检查器

> 版本：`0.1.0`
>
> 状态：Windows 桌面工具、CLI 与后台共享规则已完成本地候选；桌面发布包可用，
> 尚未部署生产 API。

赫朝整合包部署检查器用于在后台上传前检查 `.zip` 或 `.mrpack`。它只读归档，不修改
源文件、不连接 VPS，也不会创建、启动、停止或部署服务端。桌面版面向人工整理整合包，
CLI 面向脚本和 CI；两者与后台 `Hechao.Modpack` 分析器使用同一套规则。

## 1. 检查结果

| 结果 | 含义 | 是否应上传 |
| --- | --- | --- |
| 符合部署标准 | 没有阻断或警告 | 可以进入后台上传和人工确认 |
| 需要人工复核 | 没有阻断，但存在警告 | 处理或确认警告后再上传 |
| 禁止部署 | 至少存在一个阻断项 | 修复源包并重新检查，不能强制跳过 |

静态通过不代表服务端一定能启动。首次部署后仍要核对服务端 ready、实际 Java 命令、
Velocity forwarding、插件/模组加载、正确客户端进服、错误客户端拒绝、指标和世界备份。

## 2. 桌面版

运行 `Hechao.Modpack.Inspector.exe` 后，可把一个 ZIP/MRPACK 拖入窗口，或使用“选择
整合包”。结果页提供：

- 阻断、警告和通过检查项，并将阻断项优先显示；
- 每项问题的路径、原因和修复建议；
- 档案 ID、版本、Minecraft、加载器、客户端/服务端文件统计；
- 声明核心、实际启动核心、Java 命令和归档 SHA-256；
- 可脱敏留档的 JSON 报告。报告只记录归档文件名，不记录本机完整源路径。

重新打包后使用“重新检查”，不要依据旧报告上传新文件。SHA-256 不同即视为另一个
归档。

## 3. CLI

```powershell
Hechao.Modpack.Check.exe <整合包.zip|mrpack> `
  --json <部署检查报告.json> `
  --quiet
```

`--json -` 把 JSON 写到标准输出；`--quiet` 隐藏人类可读摘要。退出码为：

| 退出码 | 结果 |
| --- | --- |
| `0` | 符合部署标准 |
| `1` | 需要人工复核 |
| `2` | 禁止部署 |
| `3` | 参数、文件、权限、损坏归档或检查执行失败 |

CI 应把 `1` 和 `2` 都视为需要停止自动发布；是否接受警告必须由管理员显式决定。

## 4. 标准归档元数据

规范包在根目录提供 `hechao-pack.json`，客户端、服务端和共享内容分别位于声明目录。
`serverCore` 使用实际要启动的服务端核心，而不是仅填写客户端加载器：

```json
{
  "schemaVersion": 1,
  "id": "example-neoforge-1.21.1",
  "displayName": "示例活动",
  "version": "1.0.0",
  "minecraftVersion": "1.21.1",
  "javaMajorVersion": 21,
  "loader": "NeoForge",
  "loaderVersion": "21.1.228",
  "serverCore": "Arclight",
  "clientRoot": "client",
  "serverRoot": "server",
  "sharedRoot": "shared"
}
```

`serverCore` 当前只接受：`Vanilla`、`Paper`、`Purpur`、`Fabric`、`Forge`、
`NeoForge`、`Arclight`。缺少声明会进入“需要人工复核”；不支持的值直接阻断。

## 5. 服务端强制检查

服务端根目录至少包含：

- `server.properties`，明确设置 `server-ip=127.0.0.1` 和 `online-mode=false`；
- `eula.txt`，在确认 Mojang EULA 后设置 `eula=true`；
- `user_jvm_args.txt`，各有且只有一个 `-Xms` 和 `-Xmx`；
- `start.bat`，包含独立一行
  `if not defined HECHAO_MANAGED_START pause`；
- `start.bat` 中唯一的 Java 服务端命令，以及该命令引用的 JAR 或 `win_args.txt`。

启动命令必须使用 `java` 或 `java.exe`，由受管 runner 注入 Java；不得写制作者电脑的
绝对 `java.exe` 路径。声明核心、归档核心文件和实际启动核心必须一致。

Arclight 服务端可以同时包含 NeoForge libraries，但必须通过 Arclight JAR 启动：

```bat
java @user_jvm_args.txt -jar arclight-neoforge-1.21.1.jar nogui
```

以下写法会绕过 Arclight，并由 `ARCLIGHT_BYPASSED` 阻断：

```bat
java @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.228/win_args.txt nogui
```

仅把 Arclight JAR 放进目录不会加载 Bukkit 插件或 Arclight 的 Velocity forwarding
mixin；检查器以实际 Java 命令为准。

## 6. 构建与验证

仓库固定使用 `.NET SDK 10.0.302`。本机 SDK 路径为示例，其他环境使用满足
`global.json` 的 `dotnet`：

```powershell
& 'C:\Users\Administrator\.dotnet\dotnet.exe' test `
  tests\Hechao.Modpack.Tests\Hechao.Modpack.Tests.csproj -c Release

& 'C:\Users\Administrator\.dotnet\dotnet.exe' test `
  tests\Hechao.Modpack.Inspector.Tests\Hechao.Modpack.Inspector.Tests.csproj -c Release

& 'C:\Users\Administrator\.dotnet\dotnet.exe' publish `
  src\Hechao.Modpack.Inspector\Hechao.Modpack.Inspector.csproj `
  -c Release -r win-x64 --self-contained true
```

离屏 WPF 测试会在默认和最小窗口尺寸验证真实渲染；设置
`HECHAO_INSPECTOR_RENDER_DIRECTORY` 时同时输出 PNG，且不会接管用户桌面。

## 7. 规则边界

检查器不能静态证明第三方插件/模组兼容、世界可加载、Java 大版本真的适配、首次启动
下载可用、转发密钥正确或玩法性能合格。归档通过后仍按
[`PACKAGE_IMPORT_OPERATIONS.md`](PACKAGE_IMPORT_OPERATIONS.md) 和
[`HECHAO_NEW_SERVER_BASELINE.md`](HECHAO_NEW_SERVER_BASELINE.md) 完成真实部署验收。

## 8. 0.1.0 本地制品

Windows x64 自包含发布包不要求玩家预装 .NET，包含桌面版、CLI、中文使用说明和
IconPark 的 `LICENSE` / `NOTICE`：

```text
artifacts/modpack-inspector/release/赫朝整合包部署检查器-0.1.0-win-x64.zip
```

- 大小：`93,880,466` 字节；
- ZIP SHA-256：`3C9C0E4571738ADBF619F726C9CB60BD29ECF33D7731E7828CF71645765EBAF8`；
- 桌面 EXE SHA-256：`FF15BFC304CA4F2B0D7E677344D0ED01685DE9DDAF65841933B5740C89CE8D47`；
- CLI EXE SHA-256：`C221A69D140C73E5C253F98EB714B835EB93032E84624B7342D594ACF5440A36`。

Release 完整解决方案串行测试 `805/805` 通过；桌面 ViewModel 与离屏 WPF 渲染测试
`3/3` 通过，覆盖 `1180x780` 和 `960x640`；最终 ZIP 已独立解压，发布版 CLI 启动与
退出码已复核。该制品是本地交付工具，不代表生产 API 已更新。

# 客户端分发与签名操作手册

> 启动器正式版本：`0.14.2`
> 当前发布器：手工管理命令与独立 Package Publisher Agent 均为正式版本 `1.1.0`
> 当前状态：私有 OSS Bucket、下载域名 CNAME/HTTPS、读写分离 RAM 身份、本地鉴权下载链和生产签名信任链均已完成；基础、Vanilla、Forge、NeoForge 活动、恐怖整蛊 Fabric（历史档案 ID `pvp-fabric-1.20.1`）与 DollNight 六份档案由 API `0.27.0` 托管，启动器 `0.14.2` 已完成退出状态刷新、日志配置恢复、启动检查控制、隐私受限运行遥测、NeoForge 物理原生运行目录与私有 OSS 发布闭环。后台整合包导入只写 `Test` 通道，不能自动推进 Gray 或 Production。
>
> owl9 边界：上述恐怖整蛊档案只对应 `C:\mc\server`，不对应
> `E:\MinecraftServer` 的真正 PVP 服。

## 1. 安全边界

- 私钥只在离线管理员电脑生成和使用，不上传 API、OSS、游戏 VPS，不写入 Git，也不打包进启动器。
- 启动器只内置公钥信任包。清单必须由受信私钥签名，且清单摘要可再写入目录数据库。
- 对象文件使用 SHA-256 内容寻址；下载完成后启动器独立重算哈希。
- 清单对象 URL 必须使用 HTTPS，仅回环地址的本地测试允许 HTTP。
- `GET /v1/profiles/{id}/manifest` 始终要求有效玩家会话，并再次按目录权限确认档案可见。
- 清单中的对象 URL 指向启动器 API。API 只为已登录、具备该档案 LuckPerms 权限且对象 SHA-256 存在于已发布清单的玩家签发 5 分钟 OSS V4 URL。
- Bearer 令牌只允许发送到启动器 API 的同源地址；302 跳转到 OSS 时会被移除，`Range` 断点请求会保留。
- OSS Bucket `hechaoworld` 保持私有，并启用 Bucket 级“阻止公共访问”；启动器和清单均不保存 RAM 密钥。
- `Assets/distribution-trust.json` 当前只信任 `release-2026-07-primary`；私钥不在仓库、启动器、API、OSS 或游戏 VPS 中。

## 2. 本地验证

```powershell
dotnet build Hechao.Launcher.sln -c Release
dotnet test Hechao.Launcher.sln -c Release
```

自动化测试覆盖签名篡改、未知公钥、目录摘要锚定、路径穿越、远程 HTTP、断点续传、过期下载链接刷新、跨域令牌隔离、OSS V4 URL、OSS 不可用时保留当前版本、坏哈希与篡改修复、跨进程安装锁、版本保留、切换失败回滚、旧目录迁移、档案隔离、共享对象、玩家数据保留、每档案 Java、Java 主版本校验、特殊路径目录联接、Vanilla、Forge、Fabric 和 NeoForge 类路径、退出记录、脱敏诊断、DPAPI 凭据、对象上传、远端对象校验、发布物闭合验收、对象目录白名单、进服授权、账号安全、论坛会话联动、受控全局等级、状态心跳、授权目标定向、服务器排期和单服访问规则。`2026-07-28` 使用 .NET SDK `10.0.302` 验证完整解决方案为 `369/369` 通过，其中启动器 `96/96`；Velocity Authorizer 候选另有 `20/20`、LuckPerms 等级代理另有 `4/4` 个 Java 测试通过。

同日完成生产档案全量安装验收：从正式签名清单读取 `4,900` 个内容寻址对象，在全新目录安装 `4,902` 个档案文件并逐个重新计算 SHA-256，耗时约 76 秒。安装状态锚定清单 SHA-256 `65667E6198C3ECF75DF79C686C87C244F3D5AC21B170364BD998A1DF5111640E`，测试配置关闭缓存后残留对象缓存数为 0。随后使用该安装结果成功构建 Fabric Knot 游戏进程和 `mc.hehe11.fun` 入口参数；测试没有调用进程启动。

## 3. 生成生产签名密钥

这一步只能在确定离线保存位置和加密备份方案后执行。示例路径只是格式，不应直接照搬到仓库：

```powershell
dotnet run --project src\Hechao.Publisher -c Release -- keygen `
  --key-id release-2026-01 `
  --private-key D:\Hechao-Secrets\distribution-private.pem `
  --trust-bundle D:\Hechao-Secrets\distribution-trust.json
```

工具拒绝覆盖已有密钥。将公钥信任包内容审阅后替换 `src/Hechao.Launcher/Assets/distribution-trust.json`，私钥继续留在离线目录。

当前生产签名资产：

- Key ID：`release-2026-07-primary`
- 算法：`ECDSA_P256_SHA256`
- 公钥 SHA-256：`6D4ACA1E787CFEDA1C3A5D7B772FB1F0E03C298848538D272B12BCFAF1C94F9E`
- 主密文：`%LocalAppData%\HechaoLauncherAdmin\secrets\distribution-signing-private.dpapi`
- 本机镜像：`H:\Hechao-SecureBackup\distribution-signing-private.dpapi`

两份日常私钥副本均由 Windows DPAPI `CurrentUser` 保护，ACL 仅允许当前管理员与
`SYSTEM`，并已完成解密往返与密文一致性验证。发布器 `0.9.0` 已额外生成不依赖 DPAPI
的 RSA/AES-GCM 加密恢复包，并完成解密、临时 DPAPI 恢复、真实签名和生产公钥验签；
加密恢复包已写入私有 OSS
`backups/recovery/signing-key-v1/distribution-signing-private.hcbackup` 并完成回读
逐字节复验，恢复口令保存在另一台主机。

`0.6.0` 启动器已按 `win-x64` 单文件、自包含发布配置重建，文件版本、产品名、应用图标和内嵌信任包均已验证。发布 ZIP `artifacts/releases/Hechao-Launcher-0.6.0-win-x64.zip` 只包含 `Hechao.Launcher.exe`，SHA-256 为 `9529C175A168EDE850D4A519E50EA71268BB8A809D128FC5076F18D48D90CC0C`；EXE SHA-256 为 `0DF28FD71DA34303C1FAAC11C1D041884C4AF664D192D3D2A719FAF9A602C2E7`。历史管理端发布器 `0.5.0` 的 ZIP SHA-256 为 `176EAF4B50C36A9254E90C8B3EB5F35FAC4089095C594B3A94932B395F46B696`。

管理端发布器 `0.7.0` 正式候选基于提交 `ac7bc8045c4c5f0b10b84987b8a8cb6f02bb3fca`。它在上传前对每个内容寻址键执行 `HeadObject`，只有远端对象不存在时才上传；远端存在时必须同时匹配 `Content-Length` 与 `x-oss-meta-sha256`，不匹配立即失败且绝不覆盖。上传成功后会再次读取元数据完成闭环校验。单文件 EXE 为 `74,022,178` 字节，SHA-256 为 `78C190972D00C40A1066A6ACB21BE1624E2AF7D08F2FB128D9768E662FEC7BAC`；发布归档 `artifacts/releases/Hechao-Publisher-0.7.0-win-x64.zip` 为 `32,090,108` 字节，SHA-256 为 `E05B589976D033015D1FC05D276FE4E19694B9BD7A359569A1AE0473AF1F2F18`，只包含该 EXE。EXE 按当前决定保持 `NotSigned`。上一份归档候选 `0.6.0` 保留不变，不被本版本覆盖。完整验收记录见 [`PUBLISHER_RELEASE_0.7.0.md`](PUBLISHER_RELEASE_0.7.0.md)。

发布器 `0.9.0` 增加生产签名密钥加密导出和 DPAPI 恢复命令。导出和恢复都会先用
`Assets/distribution-trust.json` 核对 P-256 公钥，拒绝错误 Key ID 或公钥；临时私钥
字节和恢复口令字符在使用后清零。生产恢复演练与边界见
[`PUBLISHER_RELEASE_0.9.0.md`](PUBLISHER_RELEASE_0.9.0.md)。

启动器程序和游戏档案独立发布。`0.6.0` 只改变登录器与进服授权流程，没有修改 `base-1.21.11` / `1.0.5` 的 874 MB 游戏文件清单，因此玩家不应为本次启动器升级重新下载完整客户端。

## 4. 生成客户端发布物

每个档案使用独立源目录。源目录不能包含私钥、输出目录、符号链接、`.hechao` 或 `.hechao-install.json`。

### 4.1 后台整合包 Publisher Agent

Publisher `1.0.0` 增加独立代理模式。它只领取管理员已经确认、仍持有有效租约
的整合包任务；签名私钥、OSS 写凭据和明文代理令牌均只存在于同一 Windows 管理账号的
DPAPI 边界，不进入 API 或游戏 VPS。代理上传后仍由 API 使用内嵌公钥重新验签，并且只
创建 `Test` 通道发布，不触碰 `Gray` 或 `Production`。

Publisher `1.1.0` 保留上述协议并把生产代理迁移到 API 同机阿里云 systemd。令牌、
签名私钥和 OSS 写凭据使用主机绑定的 `systemd-credentials`，Windows DPAPI 实例停止
保留为回滚。Linux 工作空间会按压缩包大小和展开倍率预留磁盘，空间不足时继续心跳与
续租但不开始下载。正式部署见
[`PUBLISHER_RELEASE_1.1.0.md`](PUBLISHER_RELEASE_1.1.0.md)。

代理安装、配置、续租、缓存恢复和回滚见
[`PACKAGE_IMPORT_OPERATIONS.md`](PACKAGE_IMPORT_OPERATIONS.md)。手工 `publish`、`verify`、
`validate-release` 与正式通道推广流程继续有效，自动导入不能绕过这些发布边界。
Publisher 专项测试 `39/39`、完整解决方案 `622/622` 和只含 Publisher EXE 的
`win-x64` 自包含发布验证已通过。生产代理为单实例计划任务，固定试包复核并跳过 `4`
个已存在对象、上传新对象 `0` 个，API 再次验签后只把测试版本设到 `Test=100%`；Gray
与 Production 未变化。正式记录见
[`PUBLISHER_RELEASE_1.0.0.md`](PUBLISHER_RELEASE_1.0.0.md) 和
[`evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json`](evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json)。

管理端发布器使用 `src/Hechao.Publisher/Properties/PublishProfiles/win-x64.pubxml` 构建自包含单文件。该配置明确关闭裁剪；裁剪会移除当前 JSON 序列化元数据，导致签名清单或 DPAPI 凭据命令在运行时失败。构建后不能只检查 `--help`，必须用正式信任包执行一次 `validate-release`。

Publisher 与 `Hechao.Backup` CLI 共同引用 `Hechao.Backup.Core` 加密信封类库，互不
引用对方的可执行入口。三者目标框架必须一致，并且每次候选都要真实执行 `win-x64`
自包含单文件发布，确认 Publisher 目录不会夹带 Backup CLI，不能只依赖普通项目构建。

活动 NeoForge 源必须先通过仓库工具从 PCL 隔离目录制作。工具不会修改原客户端，会排除账号缓存、日志、世界、截图、语音设备与玩家音量配置，并强制只保留与活动服同 SHA-256 的 Meccha：

```powershell
.\tools\Prepare-NeoForgeActivityProfile.ps1 `
  -SourceMinecraftRoot "H:\MC\画画躲猫猫\.minecraft" `
  -ServerMecchaJar "C:\Hechao-Inputs\meccha_chameleon-1.21.11-neoforge-1.0.8-hotfix-zh_cn-lobby-compatible-nudge-limit-modes-watch-ui.jar" `
  -OutputDirectory artifacts\client-sources\activity-neoforge-1.21.11-1.0.10
```

```powershell
dotnet run --project src\Hechao.Publisher -c Release -- publish `
  --source artifacts\client-sources\activity-neoforge-1.21.11-1.0.10 `
  --output artifacts\distributions\activity-neoforge-1.21.11-1.0.10 `
  --profile-id activity-neoforge-1.21.11 `
  --version 1.0.10 `
  --minecraft-version 1.21.11 `
  --java-version 21 `
  --loader NeoForge `
  --loader-version 21.11.42 `
  --object-base-url https://launcher-api.hechao.world/v1/profiles/activity-neoforge-1.21.11/ `
  --key-id release-2026-07-primary `
  --private-key-dpapi "$env:LOCALAPPDATA\HechaoLauncherAdmin\secrets\distribution-signing-private.dpapi" `
  --dpapi-entropy-label HechaoLauncherAdmin/DistributionSigningPrivate/v1
```

输出结构：

```text
objects/<sha256前两位>/<sha256>
manifests/<profile-id>.json
```

发布前再次离线验签：

```powershell
dotnet run --project src\Hechao.Publisher -c Release -- verify `
  --manifest artifacts\distributions\activity-neoforge-1.21.11-1.0.10\manifests\activity-neoforge-1.21.11.json `
  --trust-bundle src\Hechao.Launcher\Assets\distribution-trust.json
```

验签后必须把清单与整个对象目录一起验收。该命令逐个重算对象 SHA-256，并拒绝缺失对象、多余旧对象、长度不符或 URL 哈希不一致：

```powershell
dotnet run --project src\Hechao.Publisher -c Release -- validate-release `
  --distribution artifacts\distributions\activity-neoforge-1.21.11-1.0.10 `
  --manifest artifacts\distributions\activity-neoforge-1.21.11-1.0.10\manifests\activity-neoforge-1.21.11.json `
  --trust-bundle src\Hechao.Launcher\Assets\distribution-trust.json
```

`--object-base-url` 必须指向同一档案的 API 目录。最终对象 URL 形如：

```text
https://launcher-api.hechao.world/v1/profiles/<profile-id>/objects/<sha256前两位>/<sha256>
```

生产发布使用 DPAPI 密文私钥，不把 PEM 写入磁盘或命令行：

```powershell
.\Hechao.Publisher.exe publish `
  --source artifacts\client-sources\base-1.21.11-1.0.5 `
  --output artifacts\distributions\base-1.21.11-1.0.5 `
  --profile-id base-1.21.11 `
  --version 1.0.5 `
  --minecraft-version 1.21.11 `
  --java-version 21 `
  --loader Fabric `
  --loader-version 0.19.2 `
  --object-base-url https://launcher-api.hechao.world/v1/profiles/base-1.21.11/ `
  --key-id release-2026-07-primary `
  --private-key-dpapi "$env:LOCALAPPDATA\HechaoLauncherAdmin\secrets\distribution-signing-private.dpapi" `
  --dpapi-entropy-label HechaoLauncherAdmin/DistributionSigningPrivate/v1
```

当前正式档案：

- 干净源：`artifacts/client-sources/base-1.21.11-1.0.5`，来自 `H:\MC\赫朝客户端`，原目录未修改。
- 档案：`base-1.21.11` / `1.0.5` / Minecraft `1.21.11` / Fabric `0.19.2` / Java `21`。
- 清单：`artifacts/distributions/base-1.21.11-1.0.5/manifests/base-1.21.11.json`。
- 清单 SHA-256：`65667E6198C3ECF75DF79C686C87C244F3D5AC21B170364BD998A1DF5111640E`。
- 逻辑文件：`4,902` 个，去重对象：`4,900` 个，总大小：`874,147,856` 字节。
- 清单已使用生产信任包验签，并对每个对象重新校验路径、长度、SHA-256 和 URL。

NeoForge 活动档案已于 `2026-07-24` 正式发布：

- 干净源：`artifacts/client-sources/activity-neoforge-1.21.11-1.0.10`；原 `H:\MC\画画躲猫猫` 未修改。
- 档案：`activity-neoforge-1.21.11` / `1.0.10` / Minecraft `1.21.11` / NeoForge `21.11.42` / Java `21`；线上占位版本从 `1.0.9` 原子升级。
- 清单：`artifacts/distributions/activity-neoforge-1.21.11-1.0.10/manifests/activity-neoforge-1.21.11.json`。
- 清单 SHA-256：`0E059BBFE9FAB6770204DE547567CA64420A45E8364FA93206BB316E8AE2B69F`。
- 逻辑文件与去重对象均为 `4,754` 个，总大小 `621,732,083` 字节；清单大小 `2,098,066` 字节。
- Meccha 仅有一份，SHA-256 为 `C72511BEF3B0CC2C1A1C97E1C33709901714460191F9549FD461E71215534E9E`，与活动服 `watch-ui` JAR 一致。
- 已使用生产信任包验签，从本地对象全新安装后逐文件复验，并成功构建 `net.neoforged.fml.startup.Client`、NeoForge `21.11.42` 与 `mc.hehe11.fun` 参数；没有启动 Minecraft。
- 生产验收确认 Member 无权取得活动清单、Participant 可以取得签名清单；全部 `203` 个新增对象和 `12` 个共享对象样本均从 OSS 下载并重算 SHA-256。活动服始终保持 `Closed 0/30`。完整证据见 [`ACTIVITY_PROFILE_RELEASE_1.0.10.md`](ACTIVITY_PROFILE_RELEASE_1.0.10.md)。

恐怖整蛊 Fabric 档案（历史 ID `pvp-fabric-1.20.1`）已于 `2026-07-25` 正式发布：

- 干净源：`artifacts/client-sources/pvp-fabric-1.20.1-1.0.0`，来自 `H:\MC\Minecraft 1.20.1 Fabric - 玩家客户端`；日常客户端原目录未修改。
- 档案：`pvp-fabric-1.20.1` / `1.0.0` / Minecraft `1.20.1` / Fabric `0.16.14` / Java `17`。
- 清单：`artifacts/distributions/pvp-fabric-1.20.1-1.0.0/manifests/pvp-fabric-1.20.1.json`。
- 清单 SHA-256：`A5BCBBA71C69E85F0ACE4000C1983F8C9C1C1D7F546AFA36C53AE39C895706E6`。
- 逻辑文件 `3,749` 个、`885,821,291` 字节；去重对象 `3,748` 个、`862,792,438` 字节。
- 生产上传新增 `3,547` 个对象、`764,553,396` 字节，另外 `201` 个对象通过长度与 SHA-256 元数据校验后跳过。
- 线上服务器显示名为“恐怖整蛊”，最低等级 `Participant`，Velocity 目标 `pvp`，后端为 `owl9.vipi9.top:19243`。完整证据见 [`PVP_PROFILE_RELEASE_1.0.0.md`](PVP_PROFILE_RELEASE_1.0.0.md)。

Vanilla、Forge 与 DollNight 档案已于 `2026-07-27` 正式发布：

- `vanilla-1.21.11` / `1.0.0`：Minecraft `1.21.11`、Vanilla、Java `21`，`4,671` 个文件与对象、`549,101,696` 字节，清单 SHA-256 `C22DEDC09576273B6D4C52B07CF7975D09BA758533B7395974BE34F73344C865`。该档案已绑定 Survival1。
- `forge-1.20.1` / `1.0.0`：Minecraft `1.20.1`、Forge `47.4.0`、Java `17`，`3,667` 个文件与对象、`725,771,107` 字节，清单 SHA-256 `D33FF592B115667713BCC87477710AA7D8A86F77490C23B70B7DEE620A56919C`。该档案已发布但未绑定服务器，不会出现在玩家目录。
- `dollnight-1.21.11` / `1.0.0`：Minecraft `1.21.11`、Fabric `0.19.2`、Java `21`，`4,902` 个逻辑文件、`4,900` 个去重对象和 `874,147,856` 字节，清单 SHA-256 `6D0C73C2B8CD34621C5D44212047DC562AD05E8277B1F195BDAC0FDA5DA16575`。该档案已绑定 DollNight。
- 三份档案均完成生产信任验签、发布物闭合校验、隔离全量安装、逐文件复验和“不启动游戏”的进程构建。生产权限回归确认匿名拒绝、Member 仅能取得有权且已绑定的档案、Participant 可取得 DollNight；对象 302 下载后长度与 SHA-256 一致。
- 生产发布前备份位于 `/var/backups/hechao-launcher/profile-publication/20260726T182024Z`。发布期间 API 未重启，公网 `/healthz`、`/readyz` 正常，warning 及以上日志为 `0`。
- 完整证据见 [`VANILLA_PROFILE_RELEASE_1.0.0.md`](VANILLA_PROFILE_RELEASE_1.0.0.md)、[`FORGE_PROFILE_RELEASE_1.0.0.md`](FORGE_PROFILE_RELEASE_1.0.0.md) 与 [`DOLLNIGHT_PROFILE_RELEASE_1.0.0.md`](DOLLNIGHT_PROFILE_RELEASE_1.0.0.md)。

### 启动器 0.11.2 下载链路

基础档案包含 `4,902` 个逻辑文件和 `4,900` 个去重对象，其中 `4,355` 个对象小于 64 KiB。旧版逐对象串行执行“API 鉴权 -> 302 -> OSS”时，生产实测只有约 `15 KiB/s`。`0.11.2` 改为最多 16 路受控并行，按 SHA-256 去重任务，聚合单调进度，并保留 Range 续传、逐对象哈希和原子目录切换。

游戏对象下载使用不经过系统代理的独立 `HttpClient`；登录、目录和普通 API 仍遵循 Windows 代理设置。临时网络中断、服务端 429/5xx 或提前断流最多重试 5 次，采用指数退避、随机抖动并尊重 `Retry-After`。Bearer 仍只发送给 `launcher-api.hechao.world`，不会随 302 跳转到 OSS。

API `0.11.1` 的 `downloads` 策略按赫朝账号分区，令牌桶容量 `192`、每秒补充 `80`；全局每 IP 每分钟 `6000` 和登录每 IP 每分钟 `10` 的限制保持不变。拒绝响应带 `Retry-After`。该策略只用于已授权且属于签名清单的对象入口，不放宽注册、登录、管理员或论坛内部端点。

生产续传从已有 `222,431,031` 字节对象缓存开始，在 `94.64` 秒内完成剩余对象并原子安装。最终 `.minecraft` 为 `4,902` 个文件、`874,147,856` 字节，安装状态为 `base-1.21.11` / `1.0.5`，无残留 `.part`。完整记录见 [`LAUNCHER_RELEASE_0.11.2.md`](LAUNCHER_RELEASE_0.11.2.md)。

`0.11.3` 不改变对象协议、签名、并发数、续传或哈希规则，只把真实进度值映射为 180 ms 的平滑显示动画，并尊重 Windows“在控件和元素内显示动画”设置。完整记录见 [`LAUNCHER_RELEASE_0.11.3.md`](LAUNCHER_RELEASE_0.11.3.md)。

`0.11.4` 只修正账号页签右边框的 WPF 像素裁切，不改变下载、安装、签名、续传、缓存或对象协议。完整记录见 [`LAUNCHER_RELEASE_0.11.4.md`](LAUNCHER_RELEASE_0.11.4.md)。

`0.11.5` 是未上传、未打标签的本机失败候选，其页签标题居中属性误使登录和注册表单按最小宽度排列。`0.11.6` 将页签标题与选中内容的对齐彻底分离，标题保持居中，表单明确横向拉伸；下载、安装、签名、续传、缓存和对象协议均未改变。完整记录见 [`LAUNCHER_RELEASE_0.11.6.md`](LAUNCHER_RELEASE_0.11.6.md)。

## 5. OSS 与 API 配置

当前云端基线：

- Bucket：`hechaoworld`，地域 `cn-shanghai`，ACL 私有。
- Bucket 级“阻止公共访问”：已开启。
- Bucket 版本控制：已开启。OSS 在版本控制已开启或暂停时会忽略 `x-oss-forbid-overwrite`，因此该请求头不能作为当前 Bucket 的不可覆盖保证。
- 自定义域名：`download.hechao.world`，CNAME 已生效。
- HTTPS：DigiCert 证书已部署到 OSS，有效期至 `2026-10-20`；TLS 与 CNAME 已完成验证。

API 只从 systemd 环境读取 RAM 凭据：

```text
OSS_ACCESS_KEY_ID
OSS_ACCESS_KEY_SECRET
```

应用配置：

```text
Distribution__ManifestDirectory=/var/lib/hechao-launcher-api/manifests
Distribution__MaximumManifestBytes=8388608
Distribution__OssRegion=cn-shanghai
Distribution__OssBucket=hechaoworld
Distribution__OssEndpoint=https://download.hechao.world
Distribution__OssObjectPrefix=objects
Distribution__PresignedUrlSeconds=300
```

[`configure-distribution.sh`](../deploy/linux/configure-distribution.sh) 从标准输入读取 AccessKey ID 和 Secret，写入权限 `600` 的环境文件，并创建只读清单目录；脚本不会重启 API。

截至 `2026-07-26`，API 专用 RAM 用户 `hechao-launcher-distribution` 已绑定自定义策略 `HechaoLauncherOssObjectRead`。策略仅允许对 `acs:oss:*:*:hechaoworld/objects/*` 执行 `oss:GetObject`；凭据已写入 API 主机环境文件，文件权限为 `root:root 600`。线上 API `0.12.0` 已读取并使用该分发配置。

上传端使用独立 RAM 用户 `hechao-launcher-publisher` 和策略
`HechaoLauncherOssObjectPublish`。当前默认 v5 只允许对
`acs:oss:*:*:hechaoworld/objects/*`、
`acs:oss:*:*:hechaoworld/releases/launcher/*`、
`acs:oss:*:*:hechaoworld/backups/database/*`、
`acs:oss:*:*:hechaoworld/backups/services/*` 与
`acs:oss:*:*:hechaoworld/backups/recovery/*` 执行 `oss:GetObject` 与
`oss:PutObject`；仍不允许列举 Bucket、读取其他前缀、删除对象或管理版本。
控制台二次回读已确认 v5 为默认版本。`oss:GetObject` 仅用于 `HeadObject`
元数据校验、私有安装包链接和备份下载复验。AccessKey 只以 Windows DPAPI
`CurrentUser` 密文保存在管理员电脑，并以 root-only 环境提供给异地备份服务；
明文下载文件已清理。使用方式：

```powershell
.\Hechao.Publisher.exe upload-oss `
  --distribution artifacts\distributions\base-1.21.11-1.0.5 `
  --bucket hechaoworld `
  --region cn-shanghai `
  --endpoint https://oss-cn-shanghai.aliyuncs.com `
  --object-prefix objects `
  --credential-dpapi "$env:LOCALAPPDATA\HechaoLauncherAdmin\secrets\oss-publisher-credential.dpapi" `
  --dpapi-entropy-label HechaoLauncherAdmin/OssPublisherCredential/v1 `
  --parallelism 8
```

上传器会先重新校验本地 SHA-256，再使用 `HeadObject` 读取当前版本元数据。已有对象只有在长度和 `sha256` 自定义元数据都与本地内容寻址摘要一致时才计入 `Already present`；缺失或不一致的元数据属于硬错误，不会退化为覆盖。仅远端不存在时才发送 Content-MD5、保留 SDK CRC64，并执行 `PutObject`；上传后再次 `HeadObject` 校验。`x-oss-forbid-overwrite` 仍作为未启用版本控制时的附加保护，但[阿里云 PutObject 文档](https://help.aliyun.com/en/oss/developer-reference/putobject)明确说明版本控制开启或暂停时会忽略它，因此不能单独作为当前 Bucket 的不可覆盖保证。

`2026-07-23` 首次生产上传完成：`4,900/4,900` 个对象成功写入，`0` 个既有对象，上传字节数 `874,147,706`。随后部署 API `0.4.0-20260723T051123Z`，将 `base-1.21.11` / `1.0.5` 清单以 `root:hechao-api 0640` 原子发布，并将目录逻辑大小 `874,147,856` 与清单 SHA-256 `65667E6198C3ECF75DF79C686C87C244F3D5AC21B170364BD998A1DF5111640E` 同步到数据库。

`2026-07-24` 活动档案上传提交 `4,754` 个对象和 `621,732,083` 字节。由于 Bucket 版本控制，OSS 报告 `4,754` 个上传、`0` 个已存在；其中与基础档案共享的 `4,551` 个摘要生成了同内容新版本，真正新增摘要为 `203` 个、`152,843,997` 字节。上传后对全部新增对象和共享样本完成真实下载与 SHA-256 复验，随后无重启地原子发布 `activity-neoforge-1.21.11` / `1.0.10`。

同日完成补强：RAM 策略升级为 v2，发布器 `0.7.0` 接入远端元数据校验。使用活动档案对生产 OSS 全量复验，结果为 `4,754` 个校验后跳过、`0` 个上传、`0` 上传字节；所有当前对象均匹配本地长度和 SHA-256 元数据，没有创建新对象版本。

API `0.17.0` 起，签名清单不再通过 `publish-profile.sh` 直接覆盖活动指针。
管理员后台只接收离线发布器生成的原始签名 JSON，使用 API 内嵌的只读公钥信任包
完成 Ed25519 验签，并按清单文件 SHA-256 保存为：

```text
/var/lib/hechao-launcher-api/manifests/releases/<profile-id>/<sha256>.json
```

数据库迁移 14 建立不可变发布记录及 Test、Gray、Production 三个通道。正式发布必须
依次经过签名导入、Test、Gray 和 Production；暂停问题版本会在事务中自动把受影响
通道移到上一份未暂停发布。迁移 14 存在时，旧 `publish-profile.sh` 会在写文件前
明确退出，避免绕过验签、通道修订和审计。完整后台流程见
[`ADMIN_CATALOG_OPERATIONS.md`](ADMIN_CATALOG_OPERATIONS.md)。

`2026-07-25` 发布恐怖整蛊档案时，发布器对 `201` 个共享对象执行远端长度与 SHA-256 元数据校验并跳过，只上传 `3,547` 个缺失对象。发布前数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260725T202241Z.dump`；清单快照目录为 `/var/backups/hechao-launcher/profile-publications/pre-pvp-fabric-1.0.0-20260725T202252Z`，归档 SHA-256 `CD99BB5059B58EA834B0BFF8D3A27D061C32439ED5E7D9E079ECA21DC4CBCF0F`。清单于 `2026-07-25T20:12:10.4811149+00:00` 原子激活。

### 启动器安装包内部灰度

发布器 `0.8.1` 增加独立的启动器安装包上传流程。它只接受规范版本号和固定文件名，并把安装包写入：

```text
releases/launcher/<version>/Hechao-Launcher-Setup-<version>-win-x64.exe
```

仓库中的发布 RAM 策略模板只在原有 `objects/*` 之外增加 `releases/launcher/*` 的 `oss:GetObject` 与 `oss:PutObject`；没有 Bucket 列举、删除、版本管理或其他前缀权限。安装包上传前先在本地计算 SHA-256，发现与发布记录不符时不会发起 OSS 请求。远端同名对象只有在长度、`sha256`、`release-version` 和 `original-filename` 元数据全部一致时才允许跳过；任何不一致都拒绝覆盖。新对象显式使用私有 ACL、`Content-MD5`、禁止覆盖请求头和下载文件名，上传后必须再次读取元数据确认。

内部灰度使用私有 Bucket 的短时 V4 签名地址，不建立公开目录，也不向 API 主机增加安装包写权限。链接有效期限制为 5 至 1440 分钟；链接属于临时访问能力，不得写入 Git、发布记录、长期公告或公开网页。重新分发时重新运行同一命令，校验既有对象后生成新链接：

```powershell
.\Hechao.Publisher.exe upload-launcher-release `
  --installer artifacts\installer\Hechao-Launcher-Setup-0.10.0-win-x64.exe `
  --version 0.10.0 `
  --sha256 E2E14306882EF072016F35D740D2F06A7C8D12F63FFE28DD0F6A2C07B24D4876 `
  --bucket hechaoworld `
  --region cn-shanghai `
  --endpoint https://oss-cn-shanghai.aliyuncs.com `
  --download-endpoint https://download.hechao.world `
  --credential-dpapi "$env:LOCALAPPDATA\HechaoLauncherAdmin\secrets\oss-publisher-credential.dpapi" `
  --dpapi-entropy-label HechaoLauncherAdmin/OssPublisherCredential/v1 `
  --link-minutes 1440
```

每次内部开放前都必须确认无签名直链返回拒绝访问、签名链接能够完整下载、下载字节数与 SHA-256 匹配，并记录链接到期时间但不记录链接本身。该流程只分发启动器安装包，不会修改 API、档案清单、游戏服务或现有网站。

仓库中的 `tools/Verify-PrivateLauncherDownload.ps1` 可直接读取 ACL 保护目录中的
发布结果，下载到随机临时文件并只输出 HTTP 状态、长度、SHA-256 和耗时。脚本不会
回显签名 URL，校验完成后始终删除临时安装包。

`2026-07-25` 已使用发布器 `0.8.1` 将启动器 `0.10.0` 写入固定对象键。首次上传为 `61,796,065` 字节，远端长度与 `sha256`、`release-version`、`original-filename` 元数据全部通过回读校验。匿名永久直链实测返回 `403`；24 小时签名链接完整下载后的 SHA-256 为 `E2E14306882EF072016F35D740D2F06A7C8D12F63FFE28DD0F6A2C07B24D4876`。第二次执行只校验既有对象并生成新短时链接，没有再次上传或覆盖。完整签名链接不进入文档和 Git。

`2026-07-26` 已将启动器 `0.11.6` 写入
`releases/launcher/0.11.6/Hechao-Launcher-Setup-0.11.6-win-x64.exe`。
远端回读、匿名 `403`、24 小时签名下载 `200`、`61,802,610` 字节和 SHA-256
`32E06CF9DCE0811293E1279C4C76B8B2C5C8401859FC5A84DCE64AB1227416E9`
全部通过。短时链接只保存在当前管理员账户的 ACL 保护目录，没有写入 Git。
第二次执行同版本命令只校验并跳过，没有覆盖或再次上传对象。首轮测试使用
[`PRELAUNCH_PILOT_0.11.6.md`](PRELAUNCH_PILOT_0.11.6.md)。

`2026-07-26` 已将启动器 `0.11.7` 写入
`releases/launcher/0.11.7/Hechao-Launcher-Setup-0.11.7-win-x64.exe`。
远端回读、匿名 `403`、24 小时签名下载 `200`、`61,805,936` 字节和 SHA-256
`9215849E914C125D827CF86D104D5FFEF865840AEEE6F31A0DC2DA6F1B1819EA`
全部通过。签名链接仅保存于管理员账户的 ACL 保护目录，未写入 Git。OSS 原始域名
完整下载耗时约 `1.55` 秒；自定义下载域名仍保留给播放器档案链路，内部安装包优先
使用本次已验证的私有 OSS 原始域名短时链接。

`2026-07-26` 已将启动器 `0.11.8` 写入
`releases/launcher/0.11.8/Hechao-Launcher-Setup-0.11.8-win-x64.exe`。
首次上传后再次执行同版本发布命令，结果为“对象已存在并已验证”，没有覆盖或重复上传。
匿名访问返回 `403`，24 小时原始节点签名下载返回 `200`，完整下载为
`61,816,090` 字节，SHA-256 为
`778F9F69439386E60CF5BEFA25BF7448EE4EAF92A05447EBB0A97553F5047BC0`，
本机完整回读耗时约 `1.42` 秒。短时链接只保存在管理员账户的 ACL 保护目录，
没有写入 Git、文档或长期公告。

`2026-07-26` 已将启动器 `0.11.9` 写入
`releases/launcher/0.11.9/Hechao-Launcher-Setup-0.11.9-win-x64.exe`。
首次上传后再次执行同版本发布命令，远端对象匹配并跳过，没有覆盖或重复上传。
匿名访问返回 `403`，24 小时原始节点签名下载返回 `200`，完整下载为
`61,815,081` 字节，SHA-256 为
`C80782CD522EFBCC1E0834AEE46583E8C0355B8B787B7174CAAF5C808CA19469`，
本机完整回读耗时约 `1.43` 秒。`0.11.8` 因特殊数据目录下 Fabric 类路径失效
停止分发，随后由 `0.11.10` 替换 `0.11.9`。

`2026-07-27` 已将启动器 `0.11.10` 写入
`releases/launcher/0.11.10/Hechao-Launcher-Setup-0.11.10-win-x64.exe`。
第二次执行确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`，
24 小时原始节点签名下载返回 `200`，完整下载为 `61,819,393` 字节，SHA-256 为
`4703FEF3113418BB13DBA86F097BE45D2C66BFD020774354117A0001FAA127AA`。
签名链接只保存在管理员账户的 ACL 保护目录。

`2026-07-27` 已将启动器 `0.11.11` 写入
`releases/launcher/0.11.11/Hechao-Launcher-Setup-0.11.11-win-x64.exe`。
第二次执行确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`，
24 小时原始节点签名下载返回 `200`，完整下载为 `61,823,943` 字节，SHA-256 为
`F6687C4CBB53BEFB3DC3D8B84FFBDF0AEC589DF69D710EE4F5DF43EFD47CB894`，
耗时约 `1.12` 秒。签名链接只保存在管理员账户的 ACL 保护目录。

`2026-07-27` 已将启动器 `0.11.12` 写入
`releases/launcher/0.11.12/Hechao-Launcher-Setup-0.11.12-win-x64.exe`。
第二次执行确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`，
24 小时 Bucket 原始节点签名下载返回 `200`，完整下载为 `61,833,814` 字节，
SHA-256 为 `F54297318865995225CE8CB748C115EA4DCA8219E02AE09ABE266F783EC033D6`，
耗时约 `1.50` 秒。`--download-endpoint` 必须使用
`https://hechaoworld.oss-cn-shanghai.aliyuncs.com` 这类 Bucket 原始节点；
服务级 `https://oss-cn-shanghai.aliyuncs.com` 生成的签名地址会返回 `403`。
有效短时链接只保存在管理员账户的 ACL 保护目录。

`2026-07-27` 已将启动器 `0.11.13` 写入
`releases/launcher/0.11.13/Hechao-Launcher-Setup-0.11.13-win-x64.exe`。
第二次执行确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`，
24 小时 Bucket 原始节点签名下载返回 `200`，完整下载为 `61,868,113` 字节，
SHA-256 为 `E6BF44D9971CEF6D874368E9912158BC60B88A886C652318E94F9D4BE0FFCFE7`，
耗时约 `1.52` 秒。有效短时链接只保存在管理员账户的 ACL 保护目录。

`2026-07-27` 已将启动器 `0.11.14` 写入
`releases/launcher/0.11.14/Hechao-Launcher-Setup-0.11.14-win-x64.exe`。
第二次执行确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`，
24 小时 Bucket 原始节点签名下载返回 `200`，完整下载为 `61,866,744` 字节，
SHA-256 为 `82542FEBDD826AF4C40D8E0AFCD65990BE54A748734829FA7EC46214A27E5EDB`，
耗时约 `1.51` 秒。有效短时链接只保存在管理员账户的 ACL 保护目录。

`2026-07-28` 已将启动器 `0.11.15` 写入
`releases/launcher/0.11.15/Hechao-Launcher-Setup-0.11.15-win-x64.exe`。
第二次执行确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`，
24 小时 Bucket 原始节点签名下载返回 `200`，完整下载为 `61,867,426` 字节，
SHA-256 为 `3C9139F8F7853C370C83A14537916D73258123A8E1CB26FDBA0B0EECD3219E44`，
耗时约 `1.46` 秒。`tools/Publish-PrivateLauncherRelease.ps1` 在进程内捕获签名 URL，
只把完整结果写入管理员 ACL 保护目录，终端和 Git 不含该链接。

`2026-07-28` 已将启动器 `0.11.16` 写入
`releases/launcher/0.11.16/Hechao-Launcher-Setup-0.11.16-win-x64.exe`。
第二次执行确认远端对象匹配并跳过，没有覆盖或重复上传。新增
`tools/Test-PrivateLauncherRelease.ps1` 检查受保护结果 ACL、固定 HTTPS 主机和对象
路径；匿名访问返回 `403`，24 小时 Bucket 原始节点签名下载返回 `200`，完整下载为
`61,866,222` 字节，SHA-256 为
`6D7C9E91EA621B384633F86D6498EBBD55BF73B65516D8F24F0838CD48EA4D8A`，
最终复验耗时约 `1.51` 秒。短时链接没有进入终端、Git、文档或公告。

`2026-07-30` 已将启动器 `0.12.2` 写入
`releases/launcher/0.12.2/Hechao-Launcher-Setup-0.12.2-win-x64.exe`。
第二次执行确认远端对象的长度、版本、文件名与 SHA-256 全部匹配并跳过，没有覆盖。
匿名访问返回 `403`，两次受保护签名下载均返回 `200`；完整下载为 `61,876,002`
字节，SHA-256 为
`FEE5A53FF9A6033E96E2150E8A31D474B559581BEED14B65F939743A83C4BDCB`，
耗时分别为 `1.35` 秒和 `1.29` 秒。短时链接没有进入终端、Git、文档或公告。

`2026-07-30` 已将启动器 `0.12.3` 写入
`releases/launcher/0.12.3/Hechao-Launcher-Setup-0.12.3-win-x64.exe`。
该版本使用真实物理 `native-runs` 目录承载 LWJGL 原生 DLL，不再把目录联接作为
最终加载位置。第二次执行确认远端对象的长度、版本、文件名与 SHA-256 全部匹配并
跳过，没有覆盖。匿名访问返回 `403`，两次受保护签名下载均返回 `200`；完整下载为
`61,874,260` 字节，SHA-256 为
`18E786560AF14C246EFF84638BABBE8E1CC02CBFB1E1065AD9501468C20603C6`，
耗时分别为 `1.22` 秒和 `1.06` 秒。短时链接没有进入终端、Git、文档或公告。

## 6. 对象恢复副本

六个活动签名档案的完整对象集已从正式签名清单重建并写入 OSS 外独立系统盘：

- `26,645` 个对象引用；
- `8,944` 个去重对象，`1,955,105,906` 字节；
- 清单和对象共 `8,950` 个文件，全部通过本地与远端 SHA-256；
- 远端再次流式解压到隔离目录并校验，失败替换不会改变 `current`；
- 目标目录只允许 `root`，不复用 OSS 发布 RAM。

每次档案发布后必须刷新该恢复集。工具、恢复顺序和当前边界见
[`DISTRIBUTION_OBJECT_RECOVERY.md`](DISTRIBUTION_OBJECT_RECOVERY.md)，机器证据见
[`evidence/DISTRIBUTION_OBJECT_RECOVERY_2026-07-30.json`](evidence/DISTRIBUTION_OBJECT_RECOVERY_2026-07-30.json)。

## 7. 后续生产接入

1. [x] 为 `download.hechao.world` 签发并绑定 HTTPS 证书，验证 TLS 与 CNAME。
2. [x] 创建只允许读取 `hechaoworld/objects/*` 的专用 RAM 身份和 AccessKey，并部署到 API 主机。
3. [x] 生成离线生产签名密钥，将公钥信任包嵌入启动器，并完成签名、验签和篡改拒绝演练。
4. [x] 从现有客户端制作干净源，生成并独立校验 `base-1.21.11` / `1.0.5` 正式签名档案。
5. [x] 制作并发布 `activity-neoforge-1.21.11` / `1.0.10`、`pvp-fabric-1.20.1` / `1.0.0`、`vanilla-1.21.11` / `1.0.0`、`forge-1.20.1` / `1.0.0` 与 `dollnight-1.21.11` / `1.0.0`，保持独立 `.minecraft`、加载器和 Java 版本。
6. [x] 发布器 `0.9.0` 已制作并真实验收不依赖当前 Windows 用户配置的加密恢复包；密文已写入私有 OSS 并完成回读逐字节复验。
7. [x] 明确首版不购买 Authenticode 证书，当前 EXE 保持 `NotSigned`；玩家公告必须提供官方来源、大小和 SHA-256，未来签名作为独立版本处理。它与客户端清单签名不是同一套密钥。
8. [x] 创建仅具备 `hechaoworld/objects/*` 元数据读取与写入权限的独立发布 RAM 身份，并将 AccessKey 保存为本机 DPAPI 密文；没有列举或删除权限。
9. [x] 发布器 `0.7.0` 对版本控制 Bucket 执行 `HeadObject` 长度与 SHA-256 元数据校验；匹配则跳过，不匹配则硬失败，不再依赖 `x-oss-forbid-overwrite` 单独保证不可变性。
10. [x] 部署 API，将签名清单原子放入受限目录，并在同一发布操作中更新清单 SHA-256、总大小和版本。
11. [ ] 用真实四级账号验证未登录、越权、链接过期、断网续传、损坏修复、磁盘不足和真实回滚。
12. [ ] Minecraft API 审核已通过；真实账号验收和 Velocity `enforce` 完成后，再启用生产目录强制登录。
13. [x] 将发布 RAM 权限模板应用为 v3，使用发布器 `0.8.1` 上传启动器 `0.10.0`，验证私有直链拒绝和短时链接下载。
14. [x] 发布启动器 `0.11.8`，验证每档案 Java、特殊路径兼容、本机覆盖升级、私有对象不可变复验与原始节点短时下载。
15. [x] 发布启动器 `0.11.9`，修复特殊路径 Fabric 类路径并停止分发 `0.11.8`。
16. [x] 发布启动器 `0.11.10`，统一运行配置的分段选择器并复验窄侧栏布局。
17. [x] 发布启动器 `0.11.11`，完成玩家主动回滚、覆盖升级、私有对象不可变复验与短时整包下载。
18. [x] 发布启动器 `0.11.12`，完成玩家确认诊断上传、干净安装、覆盖升级、卸载边界、私有对象不可变复验与短时整包下载。
19. [x] 发布启动器 `0.11.13`，完成隐私受限遥测、`0.11.12` 覆盖升级、干净安装、两轮卸载、私有对象不可变复验与短时整包下载。
20. [x] 发布启动器 `0.11.14`，让启动检查开关真实生效并保留进服前强制检查，完成 `0.11.13` 覆盖升级、干净安装、两轮卸载、私有对象不可变复验与短时整包下载。
21. [x] 将发布 RAM 权限模板应用为 v4，完成数据库异地备份、两份恢复材料上传回读、告警恢复与异地主机隔离恢复演练。
22. [x] 发布启动器 `0.11.15`，补齐并校验 Minecraft 日志配置，完成 `0.11.14` 覆盖升级、干净安装、两轮卸载、私有对象不可变复验与短时整包下载。
23. [x] 将发布 RAM 权限模板应用为 v5，完成论坛与 Sub2API 加密 OSS 往返、异地主机隔离恢复、每日 timer 及失败/恢复告警验收。
24. [x] 发布启动器 `0.11.16`，修复游戏退出后的当前档案状态，完成 `0.11.15` 覆盖升级、干净安装、两轮卸载、私有对象 ACL/匿名拒绝/短时下载复验与本机覆盖安装。
25. [x] 发布启动器 `0.12.2`，完成 Activity 五项原生目录最终规范化、完整自动测试、安装版真实进服与正常退出、私有对象不可变复验和两次短时整包下载。
26. [x] 为六个活动签名档案建立 OSS 外完整对象恢复集，完成去重、损坏拒绝、失败替换保留、远端全量哈希和隔离恢复验收。
27. [x] 发布启动器 `0.12.3`，将 Activity 原生 DLL 切换到物理 `native-runs` 目录，完成 `392/392` 自动测试、`0.12.2 -> 0.12.3` 覆盖安装、安装版真实进服与正常退出、私有对象不可变复验和两次短时整包下载。

## 8. 游戏数据目录

启动器程序与游戏数据分离。默认数据根目录为 `%LocalAppData%\Hechao\GameData`，每个档案安装在：

```text
%LocalAppData%\Hechao\GameData\instances\<profile-id>\.minecraft
```

数据根目录使用：

- `instances\.<profile-id>.staging-*`：已校验但尚未启用的暂存版本。
- `instances\.<profile-id>.previous`：上一个完整活动版本。
- `shared\objects`：按 SHA-256 保存的下载缓存和 `.part` 续传文件。
- `instances\<profile-id>\runtime`：随该客户端档案安装的受管 Java 运行时。
- `instances\<profile-id>\.hechao-java.json`：受管 Java 主版本、相对可执行路径和安装时间。
- `shared\runtime`：旧版本共用 Java 的迁移来源；`0.11.8` 新安装不再从这里直接启动。
- `.hechao/locks`：同档案跨进程独占安装锁。
- `instances\<profile-id>\.hechao-install.json`：活动版本、存储结构版本、清单摘要和签名公钥标识。

档案更新采用完整目录重建。未出现在新清单中的旧模组或旧受管配置不会进入新活动目录；`saves`、截图、日志、崩溃报告、`options.txt`、`optionsof.txt` 和 `servers.dat` 始终保留，资源包和光影包保留玩家额外文件并允许清单更新同名受管文件。`assets` 与 `libraries` 在 NTFS 上优先硬链接共享对象，不支持硬链接时自动复制。

`0.11.8` 在客户端原子切换后准备档案 Java，并把该阶段映射到总进度的 `85%` 至 `100%`。首次升级会先查找旧 `shared\runtime` 和其他档案中的相同 Java 主版本，验证后复制复用；没有兼容候选时才从 Mojang/Microsoft 运行时清单下载。玩家自定义 Java 只记录在本地启动器设置中，不进入签名档案、OSS 或 API；每次保存和启动前都校验 `java -version` 与 `hechao-profile.json` 的 `javaMajorVersion` 一致。

启动器 `0.9.0` 及后续版本会把旧 `%AppData%\Hechao\instances` 或设置中的自定义旧根目录迁移为结构版本 `2`。迁移只识别真实档案目录，拒绝重解析点，失败时保留原目录并停止启动。完整目录、安装包、升级和卸载规则见 [`WINDOWS_INSTALLER_AND_STORAGE.md`](WINDOWS_INSTALLER_AND_STORAGE.md)。

## 9. 分发容量基线

2026-07-28 的生产聚合快照为 `22` 个启用账号、`0` 个停用账号。当前日更新上限按
`30` 人规划，正常灰度按 `20` 个并发下载，容量目标按 `30` 个并发下载。

六个正式档案的逻辑下载大小如下：

| 档案 | 版本 | 字节 |
| --- | --- | ---: |
| `pvp-fabric-1.20.1` | `1.0.0` | `885,821,291` |
| `base-1.21.11` | `1.0.5` | `874,147,856` |
| `dollnight-1.21.11` | `1.0.0` | `874,147,856` |
| `forge-1.20.1` | `1.0.0` | `725,771,107` |
| `activity-neoforge-1.21.11` | `1.0.10` | `621,732,083` |
| `vanilla-1.21.11` | `1.0.0` | `549,101,696` |

最坏冷安装按最大的 `885,821,291` 字节计算。以下带宽已经增加 `15%` 的协议、重试和
个体差异余量：

| 同时下载 | 全量数据 | 30 分钟完成 | 15 分钟完成 | 10 分钟完成 |
| ---: | ---: | ---: | ---: | ---: |
| `20` | `16.50 GiB` | `90.55 Mbps` | `181.10 Mbps` | `271.65 Mbps` |
| `22` | `18.15 GiB` | `99.61 Mbps` | `199.21 Mbps` | `298.82 Mbps` |
| `30` | `24.75 GiB` | `135.83 Mbps` | `271.65 Mbps` | `407.48 Mbps` |

API 只签发短时 `302`，对象字节由私有 OSS 自定义域名或原始节点直接下发，不经过
阿里云 API 进程，也不经过游戏 VPS。阿里云主机控制台的 `200 Mbps` 峰值不是 OSS
承诺带宽，因此没有拿它冒充分发容量。当前未启用 CDN，最终实际吞吐、末端网络和
重试率仍由 20 人真实灰度验证。

以上是无缓存的全量最坏情况。内容寻址缓存和增量更新会显著降低重复下载量。机器可读
输入与计算结果见
[`evidence/CLIENT_DISTRIBUTION_CAPACITY_2026-07-28.json`](evidence/CLIENT_DISTRIBUTION_CAPACITY_2026-07-28.json)。

# 客户端分发与签名操作手册

> 启动器源码版本：`0.6.0`
> 发布器源码版本：`0.5.0`
> 当前状态：私有 OSS Bucket、下载域名 CNAME/HTTPS、读写分离 RAM 身份、本地鉴权下载链、生产签名信任链、首份正式签名档案、不可变对象上传和 API `0.6.0` 在线激活均已完成。

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

自动化测试覆盖签名篡改、未知公钥、目录摘要锚定、路径穿越、远程 HTTP、断点续传、跨域令牌隔离、OSS V4 URL、坏哈希、跨进程安装锁、版本保留、切换失败回滚、DPAPI 凭据、对象上传、进服授权和状态心跳规则。`2026-07-23` 使用 .NET SDK `10.0.302` 验证为 `80/80` 通过；Velocity 插件另有 `7/7` 个 Java 测试通过。

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

两份私钥副本均由 Windows DPAPI `CurrentUser` 保护，ACL 仅允许当前管理员与 `SYSTEM`，并已完成解密往返与密文一致性验证。`H:` 镜像仍依赖同一 Windows 用户配置，只属于本机冗余，不能代替离机恢复副本。

`0.6.0` 启动器已按 `win-x64` 单文件、自包含发布配置重建，文件版本、产品名、应用图标和内嵌信任包均已验证。发布 ZIP `artifacts/releases/Hechao-Launcher-0.6.0-win-x64.zip` 只包含 `Hechao.Launcher.exe`，SHA-256 为 `9529C175A168EDE850D4A519E50EA71268BB8A809D128FC5076F18D48D90CC0C`；EXE SHA-256 为 `0DF28FD71DA34303C1FAAC11C1D041884C4AF664D192D3D2A719FAF9A602C2E7`。管理端发布器保持 `0.5.0`，其 ZIP SHA-256 为 `176EAF4B50C36A9254E90C8B3EB5F35FAC4089095C594B3A94932B395F46B696`。

启动器程序和游戏档案独立发布。`0.6.0` 只改变登录器与进服授权流程，没有修改 `base-1.21.11` / `1.0.5` 的 874 MB 游戏文件清单，因此玩家不应为本次启动器升级重新下载完整客户端。

## 4. 生成客户端发布物

每个档案使用独立源目录。源目录不能包含私钥、输出目录、符号链接、`.hechao` 或 `.hechao-install.json`。

```powershell
dotnet run --project src\Hechao.Publisher -c Release -- publish `
  --source D:\Hechao-Builds\activity-neoforge-1.21.11 `
  --output D:\Hechao-Releases\activity-1.0.0 `
  --profile-id activity-neoforge-1.21.11 `
  --version 1.0.0 `
  --minecraft-version 1.21.11 `
  --java-version 21 `
  --loader NeoForge `
  --loader-version 21.11.42 `
  --object-base-url https://launcher-api.hechao.world/v1/profiles/activity-neoforge-1.21.11/ `
  --key-id release-2026-01 `
  --private-key D:\Hechao-Secrets\distribution-private.pem
```

输出结构：

```text
objects/<sha256前两位>/<sha256>
manifests/<profile-id>.json
```

发布前再次离线验签：

```powershell
dotnet run --project src\Hechao.Publisher -c Release -- verify `
  --manifest D:\Hechao-Releases\activity-1.0.0\manifests\activity-neoforge-1.21.11.json `
  --trust-bundle D:\Hechao-Secrets\distribution-trust.json
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

## 5. OSS 与 API 配置

当前云端基线：

- Bucket：`hechaoworld`，地域 `cn-shanghai`，ACL 私有。
- Bucket 级“阻止公共访问”：已开启。
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

截至 `2026-07-23`，API 专用 RAM 用户 `hechao-launcher-distribution` 已绑定自定义策略 `HechaoLauncherOssObjectRead`。策略仅允许对 `acs:oss:*:*:hechaoworld/objects/*` 执行 `oss:GetObject`；凭据已写入 API 主机环境文件，文件权限为 `root:root 600`。线上 API `0.6.0` 已读取并使用该分发配置。

上传端使用独立 RAM 用户 `hechao-launcher-publisher` 和策略 `HechaoLauncherOssObjectPublish`。该策略只允许对 `hechaoworld/objects/*` 执行 `oss:PutObject`，不允许读取、列举、覆盖或删除；AccessKey 只以 Windows DPAPI `CurrentUser` 密文保存在管理员电脑，密文镜像位于 `H:\Hechao-SecureBackup`，明文下载文件已清理。使用方式：

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

上传器会重新校验所有对象，发送 Content-MD5，保留 SDK CRC64，并设置 `x-oss-forbid-overwrite`。重复对象只能以 OSS 的“已存在”结果跳过，不能覆盖既有内容。

`2026-07-23` 首次生产上传完成：`4,900/4,900` 个对象成功写入，`0` 个既有对象，上传字节数 `874,147,706`。随后部署 API `0.4.0-20260723T051123Z`，将 `base-1.21.11` / `1.0.5` 清单以 `root:hechao-api 0640` 原子发布，并将目录逻辑大小 `874,147,856` 与清单 SHA-256 `65667E6198C3ECF75DF79C686C87C244F3D5AC21B170364BD998A1DF5111640E` 同步到数据库。

## 6. 后续生产接入

1. [x] 为 `download.hechao.world` 签发并绑定 HTTPS 证书，验证 TLS 与 CNAME。
2. [x] 创建只允许读取 `hechaoworld/objects/*` 的专用 RAM 身份和 AccessKey，并部署到 API 主机。
3. [x] 生成离线生产签名密钥，将公钥信任包嵌入启动器，并完成签名、验签和篡改拒绝演练。
4. [x] 从现有客户端制作干净源，生成并独立校验 `base-1.21.11` / `1.0.5` 正式签名档案。
5. [ ] 将生产签名密钥制作一份不依赖当前 Windows 用户配置的离机恢复副本。
6. [ ] 为 Windows 启动器配置独立的 Authenticode 代码签名；当前 EXE 为 `NotSigned`，它与客户端清单签名不是同一套密钥。
7. [x] 创建只允许新增 `hechaoworld/objects/*` 的独立发布 RAM 身份，并将 AccessKey 保存为本机 DPAPI 密文。
8. [x] 上传 `objects/`；对象键不可覆盖，重复 SHA-256 只保留一份。
9. [x] 部署 API `0.4.0`，将签名清单原子放入受限目录，并在同一发布操作中更新清单 SHA-256、总大小和版本。
10. [ ] 先发布内部测试档案，验证未登录、越权、链接过期、断网续传、损坏修复、磁盘不足和真实回滚。
11. [ ] Mojang API 审核、真实账号验收和 Velocity 最终授权完成后，再启用生产目录强制登录。

## 7. 客户端目录

每个档案安装在 `%AppData%\Hechao\instances\<profile-id>`。安装器使用：

- `.<profile-id>.staging-*`：已校验但尚未启用的暂存版本。
- `.<profile-id>.previous`：上一个完整活动版本。
- `.hechao/cache/objects`：按 SHA-256 保存的下载缓存和 `.part` 续传文件。
- `.hechao/locks`：同档案跨进程独占安装锁。
- `.hechao-install.json`：活动版本、清单摘要和签名公钥标识。

档案更新采用完整目录重建。未出现在新清单中的旧模组或旧配置不会进入新活动目录；玩家可变数据的迁移规则应在真正接入 Minecraft 启动前单独定义。

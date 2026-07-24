# Windows 代码签名

> 生效日期：2026-07-24
>
> 范围：赫朝启动器、NSIS 卸载程序和 Windows 安装包

## 1. 发布要求

面向玩家分发的 Windows 候选必须使用公网受信任的 RSA Authenticode 代码签名证书。自签名证书只可用于隔离测试，不能作为公开发布签名。

同一个候选必须覆盖三份可执行文件：

1. `artifacts\publish\win-x64\Hechao.Launcher.exe`
2. NSIS 在编译期间生成并嵌入安装包的 `Uninstall.exe`
3. `artifacts\installer\Hechao-Launcher-Setup-<version>-win-x64.exe`

客户端档案使用的 ECDSA 清单签名保护下载对象，不能替代这三份 Windows 可执行文件的 Authenticode 签名。

## 2. 签名环境

正式构建机需要：

- Windows SDK 的 x64 `signtool.exe`
- 位于 `Cert:\CurrentUser\My` 或 `Cert:\LocalMachine\My` 的 RSA 代码签名证书
- 证书可访问的私钥
- 证书颁发机构提供的 RFC 3161 时间戳地址

首次准备构建机时，从微软 NuGet 源安装仓库固定并校验过的 Windows SDK SignTool：

```powershell
.\tools\Install-WindowsSigningTools.ps1
```

安装脚本会校验下载包 SHA-256 和 `signtool.exe` 的 Microsoft Authenticode 发布者，文件只写入 Git 忽略的 `artifacts\tools`。构建脚本会优先自动发现该副本，也会查找系统 Windows SDK 中最新的 x64 SignTool。必要时可以使用环境变量指定：

```powershell
$env:HECHAO_SIGNTOOL_PATH = "C:\Program Files (x86)\Windows Kits\10\bin\<sdk-version>\x64\signtool.exe"
$env:HECHAO_SIGNING_CERT_THUMBPRINT = "<40-character-thumbprint>"
$env:HECHAO_SIGNING_TIMESTAMP_URL = "<issuer-rfc3161-url>"
```

证书默认从当前用户的 `My` 证书库读取。使用本机证书库时显式传入：

```powershell
.\tools\Build-WindowsInstaller.ps1 `
    -SigningCertificateStoreLocation LocalMachine
```

证书指纹不是秘密。PFX、硬件令牌 PIN、私钥和任何证书密码不得写入脚本、环境样例、Git、日志或发布记录。

## 3. 正式构建

```powershell
.\tools\Build-WindowsInstaller.ps1
```

正式入口默认关闭失败：

1. 预检证书用途、私钥、有效期、RSA 算法、微软可信根链、时间戳地址和 SignTool。
2. 运行解决方案测试并发布单文件启动器。
3. 使用 SHA-256 和 RFC 3161 时间戳签署启动器。
4. NSIS 使用 `!uninstfinalize` 签署卸载程序；签名命令返回非零时立即终止编译。
5. 编译安装包并签署外层安装程序。
6. 使用 SignTool 和 `Get-AuthenticodeSignature` 验证签名、发布者指纹和时间戳。
7. 全部签名通过后才生成启动器与安装包的 SHA-256 文件。

签名后不得再修改、压缩或重新封装 EXE，否则签名或已记录哈希将失效。

## 4. 内部开发构建

没有正式证书时，只能显式生成不可分发的内部测试包：

```powershell
.\tools\Build-WindowsInstaller.ps1 -SkipSigning
```

该模式会输出醒目的未签名警告。`-SkipSigning` 不得用于正式候选、OSS 上传、网站下载或玩家灰度。

## 5. 发布验收

```powershell
Get-AuthenticodeSignature `
    .\artifacts\publish\win-x64\Hechao.Launcher.exe,
    .\artifacts\installer\Hechao-Launcher-Setup-*-win-x64.exe |
    Select-Object Path, Status, SignerCertificate, TimeStamperCertificate
```

隔离目录安装后还要检查 `Uninstall.exe`。三份文件都必须满足：

- `Status` 为 `Valid`
- 发布者与本次批准的证书主体、指纹一致
- `TimeStamperCertificate` 存在
- 文件哈希与签名完成后生成的 `.sha256` 一致
- 安装、升级、启动和卸载冒烟测试通过

## 6. 证书轮换

证书续期或更换发布者身份时，先在独立候选上完成三份文件的签名和 SmartScreen 灰度，再切换正式构建机。旧证书及其私钥按颁发机构要求吊销或归档，不得删除已发布版本的哈希、签名状态和时间戳记录。

参考：

- [Microsoft SignTool](https://learn.microsoft.com/dotnet/framework/tools/signtool-exe)
- [Authenticode 时间戳](https://learn.microsoft.com/windows/win32/seccrypto/time-stamping-authenticode-signatures)
- [SmartScreen 发布者信誉](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)
- [NSIS `!uninstfinalize`](https://nsis.sourceforge.io/Docs/Chapter5.html#uninstfinalize)

# 赫朝启动器 macOS M4 版交付说明

## 支持范围

- 仅支持搭载 M4 的 Mac，发布运行时固定为 `osx-arm64`。
- 最低系统版本为 macOS 15.0。
- 不提供 Intel、Rosetta 或通用二进制版本。
- 应用包含 .NET 运行时，不要求玩家另装 .NET；Minecraft 所需 ARM64 Java 由启动器按客户端档案准备。

## 构建 `.app`

在 PowerShell 7 中运行：

```powershell
pwsh tools/macos/New-HechaoMacBundle.ps1
```

脚本执行 `dotnet publish -r osx-arm64 --self-contained true`，扫描全部 Mach-O；上游依赖如果提供 FAT Mach-O，会只抽取其中的 ARM64 slice，缺少 ARM64 slice 时直接失败。随后脚本组装标准 `.app` 目录，并生成保留 Unix 可执行权限的 ZIP 和 SHA-256 文件。在 macOS 上运行时，脚本还会执行 ad-hoc 签名；在其他系统交叉构建时，产物文件名明确标记为 `unsigned`。

## 本机测试签名

在 M4 Mac 上重新运行打包脚本即可生成 ad-hoc 签名包。ad-hoc 签名只用于开发验收，不等同于 Apple Developer ID 签名或公证，也不能消除从网络下载时的 Gatekeeper 提示。

## 正式签名与公证

先在 M4 Mac 的 Keychain 中配置 `notarytool` profile，然后设置：

```bash
export APPLE_SIGN_IDENTITY='Developer ID Application: ...'
export APPLE_NOTARY_PROFILE='hechao-notary'
bash tools/macos/sign-and-notarize.sh \
  'artifacts/macos-m4/赫朝启动器.app' \
  'artifacts/macos-m4/Hechao-Launcher-macOS-M4-v0.15.8-notarized.zip'
```

脚本会为嵌套 Mach-O、应用包启用 hardened runtime 签名，执行严格验证，提交 Apple 公证，装订 ticket，重新打包并输出 SHA-256。

当前仓库没有 Apple Developer ID 或公证凭据，因此交叉构建出的测试包不是正式签名或已公证发布包。

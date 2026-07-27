# 发布器 0.9.0 发布记录

> 状态：源码、自动测试与生产签名密钥恢复演练完成
>
> 更新日期：`2026-07-27`

## 1. 变更

发布器新增两个离线恢复命令：

```text
export-signing-recovery
restore-signing-recovery
```

导出命令从现有 PEM 或 Windows DPAPI 密文加载 ECDSA P-256 私钥，先与启动器生产信任
包核对，再在内存中导出 PKCS#8，并使用至少 3072 位 RSA-OAEP-SHA256 包装的
AES-256-GCM 分块信封加密。私钥字节在使用后清零，输出目录只出现 `.hcbackup`。

恢复命令读取已经由恢复工具解密的二进制 PKCS#8，再次与生产信任包核对，随后写入新的
Windows CurrentUser DPAPI 密文和非秘密元数据。输出使用 `CreateNew`，不会覆盖现有
生产私钥。

## 2. 自动验证

- 加密导出、解密、DPAPI 恢复、再次加载和公钥逐字节一致。
- 不匹配信任包的导出和恢复均在写出目标文件前失败。
- 加密信封不包含 `PRIVATE KEY` 明文。
- 备份信封篡改、错误恢复密钥、尾随数据和不安全路径继续被拒绝。
- 完整解决方案 `346/346` 通过。

## 3. 生产恢复演练

生产 Key ID：

```text
release-2026-07-primary
```

签名公钥 SHA-256：

```text
6D4ACA1E787CFEDA1C3A5D7B772FB1F0E03C298848538D272B12BCFAF1C94F9E
```

恢复 RSA Key ID：

```text
517949CD3B80EB25D46C33A523429C099B809EEC256EB1CE7F240FE1BFE433CD
```

加密恢复包 SHA-256：

```text
271F2311F727D81E7C29D22B096681FC9F041C97636C7CF980129035AC61722E
```

演练从生产 DPAPI 私钥导出加密恢复包，解密到受限临时目录，恢复为新的临时 DPAPI
密文，并用恢复出的密钥签署 `signing-recovery-drill` 清单。生产信任包成功验证该清单
为 `release-2026-07-primary`。随后已删除临时 PKCS#8、临时 DPAPI、测试清单和对象，
只保留加密恢复包等待写入私有 OSS 恢复前缀。

## 4. 恢复边界

- `.hcbackup` 和加密 RSA 私钥可以保存在私有 OSS。
- RSA 私钥的口令必须保存在另一台主机，不与 OSS 副本同处。
- 恢复时必须先验证对象 SHA-256，再解密到受限临时目录。
- 恢复出的 ECDSA 私钥必须与仓库信任包核对，不能仅凭 Key ID 接受。
- 演练结束必须删除明文 PKCS#8；生产 DPAPI 主文件不得被演练覆盖。

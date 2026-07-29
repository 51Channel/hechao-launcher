# 客户端对象恢复

> 当前状态：六个活动签名档案的全部内容寻址对象已建立 OSS 之外的完整恢复副本，
> 并通过本地全量哈希、远端全量哈希和隔离解压验收。该副本是独立存储介质和独立
> 凭据边界，不冒充跨云厂商或跨地域灾备。

## 1. 恢复集

恢复集由六份活动签名清单生成：

- `base-1.21.11` / `1.0.5`
- `activity-neoforge-1.21.11` / `1.0.10`
- `pvp-fabric-1.20.1` / `1.0.0`
- `vanilla-1.21.11` / `1.0.0`
- `forge-1.20.1` / `1.0.0`
- `dollnight-1.21.11` / `1.0.0`

清单共有 `26,645` 个对象引用，去重后为 `8,944` 个对象、
`1,955,105,906` 字节。恢复集同时保存签名清单、对象、`inventory.json` 和覆盖全部
文件的 `SHA256SUMS`。

## 2. 创建与校验

使用 PowerShell 7.4 或更高版本：

```powershell
.\tools\recovery\New-HechaoDistributionObjectMirror.ps1 `
    -DestinationRoot .\artifacts\recovery\distribution-object-mirror-current `
    -UseHardLinks

.\tools\recovery\Test-HechaoDistributionObjectMirror.ps1 `
    -MirrorRoot .\artifacts\recovery\distribution-object-mirror-current
```

创建器只在暂存目录完成全部校验后才替换当前恢复集。源对象损坏、目标损坏或替换失败
都会停止操作并保留上一份有效恢复集。源目录和目标目录必须完全分离，档案名、
档案 ID 与版本必须是安全的单一路径段。自动化夹具覆盖共享对象去重、完整恢复集、
损坏恢复集拒绝、损坏源拒绝、路径穿越拒绝、目录互相嵌套拒绝和失败替换保留。

## 3. 异机安装

`Install-HechaoDistributionObjectMirror.ps1` 通过严格主机密钥 SSH 把恢复集流式写入
远端临时目录，重新校验每个文件，再在第二个隔离目录完成一次解压恢复。只有两轮
验证都通过时才原子更新 `current` 符号链接。

当前生产副本位于：

```text
/var/backups/hechao-launcher/distribution-objects/20260729T203955Z
```

目录权限为 `0700 root:root`。`current` 只指向已验收版本，失败上传和失败恢复不会
改变它。该操作不重启 API、Velocity 或任何 Minecraft 服务端。

## 4. 发布规则

每次新增或替换正式档案版本后，必须：

1. 从全部活动签名清单重新构建完整恢复集。
2. 运行本地全量哈希校验。
3. 安装到独立主机并完成远端全量哈希与隔离恢复。
4. 记录对象集、`inventory.json` 和 `SHA256SUMS` 的 SHA-256。
5. 验证 `current` 已指向新版本，且没有 `.partial` 或恢复临时目录。

恢复副本不能代替 OSS 版本控制、生产签名密钥恢复包或玩家端哈希校验。跨云厂商、
跨地域副本属于后续可选增强，不得在当前证据中宣称已完成。

机器证据见
[`evidence/DISTRIBUTION_OBJECT_RECOVERY_2026-07-30.json`](evidence/DISTRIBUTION_OBJECT_RECOVERY_2026-07-30.json)。

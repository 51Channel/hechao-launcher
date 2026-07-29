# 版本发布与 Git 工作流

> 生效日期：2026-07-23
> 原则：每个功能、修复和运维改版都必须进入 Git；生产状态与源码状态必须能够互相追溯

## 1. 提交边界

每项工作至少形成一个范围清楚的提交。常用前缀：

| 前缀 | 用途 |
| --- | --- |
| `feat:` | 新功能 |
| `fix:` | 缺陷修复 |
| `ops:` | 部署、监控、备份和服务器配置 |
| `docs:` | 只修改文档 |
| `test:` | 只补测试 |
| `chore:` | 构建、依赖和仓库维护 |

同一功能的源码、测试、无秘密部署脚本和运维文档应在同一个功能提交中，或使用相邻、可独立回滚的提交。不要把无关重构、游戏服内容、生成目录和秘密混入提交。

## 2. 上线定义

一次改版只有同时满足以下条件才算完成：

1. 源码和自动化测试通过。
2. 生成发布物并核对产品版本、文件结构和 SHA-256。
3. 部署前备份完成，部署后健康检查和旧业务回归通过。
4. 文档记录发布 ID、哈希、备份、当前模式、尚未执行的重启和回滚方法。
5. `git diff --check` 通过，暂存区不含秘密、构建产物或本地运行数据。
6. 创建明确提交并推送到用户指定远端。
7. 对正式发布创建与组件对应的注释标签。
8. 面向玩家的版本同步更新安装说明，面向管理员的版本按 [`ADMIN_RELEASE_RUNBOOK.md`](ADMIN_RELEASE_RUNBOOK.md) 完成灰度与收口。

未获得 Git 远端、提交身份或推送权限时，可以完成开发和部署准备，但必须把“尚未推送”列为明确阻塞，不能声称版本已归档。

## 3. 版本与标签

组件独立版本：

```text
launcher-v0.11.16
api-v0.20.0
velocity-authorizer-v0.2.0
publisher-v0.9.0
status-collector-v0.2.0
server-metrics-agent-v0.1.0
luckperms-tier-agent-v0.1.0
platform-monitor-v0.1.2
backup-v0.1.0
world-backup-v0.1.0
profile-base-1.21.11-v1.0.5
profile-pvp-fabric-1.20.1-v1.0.0
```

标签只指向已经测试、记录并推送的提交。测试构建不占用正式版本号；已上传的对象、清单、发布归档和标签不得覆盖，应发布更高版本。

Windows 启动器正式候选统一使用：

```powershell
.\tools\Build-WindowsInstaller.ps1
```

该入口必须产出 `Hechao-Launcher-Setup-<version>-win-x64.exe` 和同名 `.sha256`。发布记录同时登记安装包和安装后 `Hechao.Launcher.exe` 的版本、大小、SHA-256、代码签名状态，以及一次隔离目录安装/卸载结果。游戏数据目录不进入安装包或 Git；卸载测试必须确认其不在删除边界内。

首版已明确不购买 Authenticode 证书，当前安装包与 EXE 的预期状态为 `NotSigned`。这不是跳过完整性验证：正式公告必须只使用官方来源，并同时给出版本、大小和 SHA-256。以后增加代码签名时应作为独立启动器版本提交、测试、构建和发布，不得覆盖已有安装包。

## 4. PowerShell 运行时

本机发布、仓库脚本和 Windows VPS 运维统一使用 PowerShell 7 的 `pwsh`，
禁止用 Windows PowerShell 5.1 的 `powershell.exe` 执行赫朝业务脚本。尚未安装
PowerShell 7 的机器只允许使用 5.1 完成一次
`Install-HechaoPowerShell7.ps1` 引导，之后立即切换到 `pwsh`。

正式 Windows 计划任务使用
`C:\Program Files\PowerShell\7\pwsh.exe`。提交或改版运维脚本前必须运行：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Test-HechaoPowerShell7Compliance.ps1
```

版本、安装校验、VPS 迁移和回滚见
[`POWERSHELL_7_OPERATIONS.md`](POWERSHELL_7_OPERATIONS.md)。

## 5. 提交前检查

```powershell
git status --short
git diff --check
git diff --cached --check
git diff --cached --stat
git remote -v
git config --get user.name
git config --get user.email
.\tools\Test-ReleaseProvenanceLedger.ps1
```

至少复核以下内容没有进入暂存区：

- Microsoft、Minecraft、赫朝会话和内部同步令牌。
- OSS AccessKey、数据库口令、SSH 私钥和证书私钥。
- `artifacts/`、`bin/`、`obj/`、Gradle `build/`、日志、数据库和崩溃转储。
- VPS 密码、RDP 密码或只能保存在本机安全存储中的路径内容。

`.gitignore` 是最后一道误操作保护，不是秘密管理方案。已经误提交的秘密必须立即吊销和轮换，不能只从后续提交删除。

活动发布统一登记在
[`docs/evidence/ACTIVE_RELEASE_PROVENANCE_2026-07-28.json`](evidence/ACTIVE_RELEASE_PROVENANCE_2026-07-28.json)。
每条记录必须包含注释标签及其落点、实际构建来源、发布人、一个主制品 SHA-256、明确
回滚目标和仓库内证据。代码制品的构建来源可以早于补齐发布记录的标签落点，两者必须
分开记录，不能拿文档提交冒充构建提交。客户端档案以签名清单 SHA-256 作为内容来源。

发布器或其他管理工具若进行追溯重建，必须标记 `reconstructedFromTag`，不得声称与
历史未归档二进制逐字节一致。今后的正式版本必须在发布收口前更新台账并让校验脚本
通过。

## 6. 发布提交模板

```text
feat: add grant-directed Velocity routing

- return the selected target when a one-time grant is consumed
- rewrite the initial lobby target to the authorized backend
- keep the production plugin in monitor mode during real-account validation
- document deployment, production smoke, rollback and remaining acceptance

Tests: dotnet 200/200; Velocity 11/11
Production: API 0.14.1 healthy; plugin 0.2.0 loaded in monitor mode
```

提交正文只记录非秘密事实。远端推送后再把提交 ID 和标签补入发布记录；若因此修改文档，应形成一个小型 `docs:` 或 `ops:` 提交。

## 7. 当前初始提交

本目录在开始执行该规则时已经包含完整的平台源码，但 Git 尚无历史。第一次提交应作为“平台初始基线”，覆盖截至 API `0.5.0`、启动器 `0.6.0` 和 Velocity 插件 `0.1.0` 的现状。此后每项功能或改版必须独立提交并推送，不能继续累积为第二个巨型提交。

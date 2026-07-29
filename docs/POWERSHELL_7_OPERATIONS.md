# PowerShell 7 运维标准

> 生效日期：2026-07-30
> 适用范围：赫朝启动器仓库、本机发布操作、owl5 与 owl9 上的赫朝计划任务

## 1. 强制版本

所有日常 `.ps1` 开发、发布、审计和 VPS 运维统一使用稳定版 PowerShell
`7.6.4`，入口为 `pwsh.exe`。不得再用 Windows PowerShell 5.1
`powershell.exe` 执行赫朝业务脚本。

唯一例外是尚未安装 PowerShell 7 的机器首次执行
`Install-HechaoPowerShell7.ps1`。这个引导步骤可以由 Windows PowerShell
5.1 启动；安装和验证成功后，后续操作必须切换到 `pwsh.exe`。

正式安装包来自
[PowerShell v7.6.4 官方发布页](https://github.com/PowerShell/PowerShell/releases/tag/v7.6.4)：

- 文件：`PowerShell-7.6.4-win-x64.msi`
- SHA-256：`D11942DF52FD12470169797ABFA4781D9480EFDC81000BA4FA55A5B921ED8DD0`
- 签名者：`Microsoft Corporation`

引导脚本在安装前同时校验 SHA-256 和 Authenticode 签名。下载通道可以替换，
校验标准不可替换。

## 2. 当前部署

| 环境 | 版本 | 路径 | 说明 |
| --- | --- | --- | --- |
| 开发机 | `7.6.4` | `%LocalAppData%\Programs\PowerShell\7\PowerShell\7\pwsh.exe` | 当前用户安装，已加入用户 PATH |
| owl5 | `7.6.4` | `C:\Program Files\PowerShell\7\pwsh.exe` | 全机安装 |
| owl9 | `7.6.4` | `C:\Program Files\PowerShell\7\pwsh.exe` | 全机安装 |

owl5 已迁移以下计划任务：

- `Hechao Launcher LuckPerms Sync`
- `Hechao-MinecraftConsoleBridge`
- `Hechao-Server-ActivityNeoForge`
- `Hechao-Server-Lobby`
- `Hechao-Server-Survival1`
- `Hechao-Server-Survival2`

迁移前 XML 备份：
`C:\ProgramData\Hechao\Backups\PowerShell7TaskMigration-20260729T161923Z`。

owl9 只迁移以下赫朝任务：

- `Hechao-HorrorPrank-WorldBackup`
- `Hechao-MinecraftConsoleBridge`

迁移前 XML 备份：
`C:\ProgramData\Hechao\Backups\PowerShell7TaskMigration-20260729T161952Z`。
owl9 的真正 PVP 服务端及其目录、进程和任务未被修改。

迁移只更新计划任务的执行器，不重启运行中的 Minecraft、Velocity 或其他 Java
进程。已经运行的任务保持原进程；下次计划任务启动时使用 PowerShell 7。
机器可读验收记录见
[`evidence/POWERSHELL_7_MIGRATION_2026-07-30.json`](evidence/POWERSHELL_7_MIGRATION_2026-07-30.json)。

## 3. 日常调用

开发机完成 PATH 刷新后：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Build-WindowsInstaller.ps1
```

VPS 计划任务固定使用完整路径：

```text
C:\Program Files\PowerShell\7\pwsh.exe
```

检查仓库引用：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Test-HechaoPowerShell7Compliance.ps1
```

审计远端计划任务：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\server\Invoke-HechaoPowerShellTaskAudit.ps1 `
    -HostName <host> `
    -Port <ssh-port> `
    -KeyPath <ssh-private-key>
```

## 4. 回滚

计划任务迁移器在任何更新或验证失败时会从 XML 自动恢复全部候选任务。迁移完成后
如需人工回滚，应从对应备份目录读取任务 XML，再用
`Register-ScheduledTask -Xml ... -Force` 恢复。回滚任务定义不要求重启已经运行的
Minecraft 进程。

PowerShell 7 本身不得在赫朝任务仍指向 `pwsh.exe` 时卸载。需要降级时先安装受支持
版本、验证脚本和计划任务，再切换路径；不得直接回退到 Windows PowerShell 5.1。

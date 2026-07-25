# Minecraft 世界备份运维

> 部署时间：2026-07-26
> 当前状态：引擎、本地与远端冒烟测试通过；首次正式计划归档待验收

## 1. 事故背景

旧脚本允许 Survival1 与 Survival2 在 04:00 并发压缩，并按全目录保留 10 份。2026-07-26 两个任务分别生成约 3.4 GB 的不完整 ZIP，把 `E:` 写满；其中一个缺少中央目录，另一个被残留上传进程占用。两个损坏归档和一个 0 字节大厅归档已在确认不可读取后清理。

一份 `7,963,944,183` 字节的历史备份已在核对文件名与大小后从 `E:\manual-backups` 迁移到 `C:\manual-backups\E-drive-overflow`。处理后剩余空间为：

- `C:`：`9,402,572,800` 字节，约 8.76 GiB
- `E:`：`14,922,022,912` 字节，约 13.90 GiB

## 2. 当前组件

| 组件 | 路径 | SHA-256 |
| --- | --- | --- |
| 备份引擎 | `C:\ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1` | `C8166E8DE97AB3CCC03B6C652266C2B4541CA05F66E0FA271366C0845F9F1DB8` |
| Survival1 包装脚本 | `E:\Survival1\backup.ps1` | `E65D99ACB64895C857AB59FF25959327C1DE9DEFF389D093102A163F3125E811` |
| Survival2 包装脚本 | `E:\Survival2\backup.ps1` | `DA466A7BA693DACDC882705F6B410FF19D8AD68CB1B99277A20F325C009F2F55` |
| Lobby 包装脚本 | `E:\LobbyServer\backup.ps1` | `73D51164D27695CFA6135900DD79D9891A1AAC71DE4A7A6204AD262FDCCC1103` |

旧脚本备份位于 `E:\manual-backups\world-backup-scripts-20260725T210711Z`；本次安全预留补强前版本另存于 `E:\manual-backups\world-backup-scripts-20260725T212608Z`。

## 3. 安全流程

引擎按以下顺序执行：

1. 获取全局互斥锁，同一时间只允许一个世界备份。
2. 验证源、目标和归档路径，拒绝越界或危险路径。
3. 按接近源文件总量的最坏压缩情况估算归档，检查目标盘可用空间并保留安全余量。
4. 排除运行时 `session.lock`。
5. 先写入唯一 `.partial` 文件。
6. 重新打开 ZIP，核对归档条目数量。
7. 计算 SHA-256，先写入临时校验文件。
8. 依次原子完成 ZIP 与 `.sha256`；校验文件未完成时删除本轮正式 ZIP。
9. 按服务器独立执行保留策略。
10. 任何失败都删除本次 `.partial`，保留既有可用归档。

Survival1 与 Survival2 当前各保留 1 份，Lobby 保留 7 份。历史大厅归档已统一加上 `lobby-` 前缀，避免跨服保留策略误删。

## 4. 触发边界

不要直接对正在写入的世界运行完整热拷贝。正式备份仍由服务器自身的 Essentials `/backup` 或等价保存/冻结流程触发包装脚本，确保区块先落盘。安装脚本只部署文件，不启动、停止或重启任何服务。

RCON 在三个 Paper 服务端均为关闭状态，因此不能用外部脚本伪造保存流程。

## 5. 验收

本地和 VPS 临时夹具均连续执行两次备份，确认：

- 每次归档包含预期 2 个文件。
- 保留策略最终只留下 1 个 ZIP 和 1 个校验文件。
- SHA-256 与归档重新计算结果一致。
- 临时测试目录已经清理。

首次正式计划任务完成后还必须核对：

```powershell
Get-ChildItem E:\backups,E:\backups-lobby -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object FullName,Length,LastWriteTime

Get-Content C:\ProgramData\Hechao\WorldBackup\*.log -Tail 100
```

对最新 ZIP 执行条目读取和 SHA-256 复核，并记录各盘剩余空间。完成这一步之前，只能写“备份引擎已部署并通过夹具测试”，不能写“生产世界备份已验收”。

## 6. 恢复

生产恢复前先停止对应后端，额外保存当前世界，验证目标归档 SHA-256，并恢复到独立临时目录。核对 `level.dat`、维度目录和关键插件数据后，才允许在维护窗口切换世界目录。不得把 ZIP 直接覆盖到运行中的世界。

# Minecraft 世界备份运维

> 首次部署：2026-07-26
> VSS 热备份升级：2026-07-27
> 当前状态：三服 Essentials 正式归档、远端保留、异机副本、完整解压与隔离恢复验收均已通过

## 1. 事故背景

旧脚本允许 Survival1 与 Survival2 在 04:00 并发压缩，并按全目录保留 10 份。2026-07-26 两个任务分别生成约 3.4 GB 的不完整 ZIP，把 `E:` 写满；其中一个缺少中央目录，另一个被残留上传进程占用。两个损坏归档和一个 0 字节大厅归档已在确认不可读取后清理。

一份 `7,963,944,183` 字节的历史备份已在核对文件名与大小后从 `E:\manual-backups` 迁移到 `C:\manual-backups\E-drive-overflow`。随后又把两个不参与运行的旧迁移制品转存到管理机 `H:`，本地重新计算 SHA-256 一致并写入旁车校验文件后，才删除 VPS 原件：

- `H:\server-backups\owl5\manual-backups\2026-07-26\HorrorPrankDirect-for-owl9-20260717-134617.zip`：`4,309,621,301` 字节，SHA-256 `498E06630C55A4D6B287DD03788CE66C675066F14E9F5134986977127BA41257`。
- `H:\server-backups\owl5\migration-artifacts\2026-07-26\survival1-migration-20260524.zip`：`3,079,340,643` 字节，SHA-256 `BD58DA95A0D1DE6EAADC620C5733993FB384D81F54794F17334C50EA63928D1C`。

处理后 VPS 剩余空间为：

- `C:`：`9,399,726,080` 字节，约 8.75 GiB。
- `E:`：`22,310,768,640` 字节，约 20.78 GiB。

三服按最坏估算连续完成所需门槛为 `18,672,100,775` 字节，当前额外余量为
`3,638,667,865` 字节，约 3.39 GiB。管理机归档不替代云端异地备份，
但迁移制品已不再占用生产世界归档盘。

## 2. 当前组件

| 组件 | 路径 | SHA-256 |
| --- | --- | --- |
| 备份引擎 | `C:\ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1` | `2CC7511C222FEE2D984FD49D150F89355D7C9C48FD7A705FDB3DB047C34CD691` |
| Survival1 包装脚本 | `E:\Survival1\backup.ps1` | `E65D99ACB64895C857AB59FF25959327C1DE9DEFF389D093102A163F3125E811` |
| Survival2 包装脚本 | `E:\Survival2\backup.ps1` | `DA466A7BA693DACDC882705F6B410FF19D8AD68CB1B99277A20F325C009F2F55` |
| Lobby 包装脚本 | `E:\LobbyServer\backup.ps1` | `73D51164D27695CFA6135900DD79D9891A1AAC71DE4A7A6204AD262FDCCC1103` |
| Survival1 定时脚本 | `E:\Survival1\plugins\Skript\scripts\daily-backup.sk` | `B37591E3C439E619F020613712EA9B71A9ACF3BCE073971564DF65AC5323D2C8` |
| Survival2 定时脚本 | `E:\Survival2\plugins\Skript\scripts\daily-backup.sk` | `C03FE09AE4E861DDDDEBC123A9E8DE45666C1003494FD0AC23ECFEE6F1BBD0B6` |
| Lobby 定时脚本 | `E:\LobbyServer\plugins\Skript\scripts\daily-backup.sk` | `A2B0758C1E3426A7622B9587E69EADC7246AE3217653DAACBB903EF014549049` |
| Lobby Essentials 配置 | `E:\LobbyServer\plugins\Essentials\config.yml` | `E6073B6E65DD5F1C6BEC35F65C3EF369E105DC465C96E255300DDAFB55AC5825` |

旧脚本备份位于 `E:\manual-backups\world-backup-scripts-20260725T210711Z`；本次安全预留补强前版本另存于 `E:\manual-backups\world-backup-scripts-20260725T212608Z`。
错峰调整前的定时脚本和 Lobby Essentials 配置位于
`E:\manual-backups\world-backup-schedule-20260726T062655Z`。
VSS 升级前的生产引擎与包装脚本位于
`E:\manual-backups\world-backup-scripts-20260727T103810Z`。隔离测试与部署证据位于
`E:\manual-backups\world-backup-vss-task-candidate-20260727T103633Z` 和
`E:\manual-backups\world-backup-vss-deploy-20260727T103748Z`。

## 3. 安全流程

引擎按以下顺序执行：

1. 使用 `active.json` 原子获取全局任务所有权；发现失效任务时只清理其中记录的精确卷影 ID 和状态文件。
2. 验证源、目标和归档路径，拒绝越界、网络盘或危险路径。
3. 按接近源文件总量的最坏压缩情况估算归档，检查目标盘可用空间并保留安全余量。
4. 在 Essentials 已完成 `save-all` 和 `save-off` 的短暂冻结窗口中创建 NTFS VSS 一致快照。
5. 后台工作进程接管卷影并完成握手后，协调进程立即退出，让 Essentials 执行 `save-on`；压缩不会让世界长时间停存。
6. 后台只读取 `\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy*`，排除运行时 `session.lock`，不直接读取被 Java 独占的实时 `.mca`。
7. 先写入唯一 `.partial` 文件，再重新打开 ZIP 核对归档条目数量。
8. 计算 SHA-256，先写入临时校验文件；依次原子完成 ZIP 与 `.sha256`。
9. 按服务器独立执行保留策略，并写入 `<server>.status.json` 与服务日志。
10. 无论成功或失败都按精确 ID 删除本轮 VSS 卷影；失败时删除 `.partial`，保留既有可用归档。

Survival1 与 Survival2 当前各保留 1 份，Lobby 保留 7 份。历史大厅归档已统一加上 `lobby-` 前缀，避免跨服保留策略误删。

## 4. 触发边界

不要直接对正在写入的世界运行完整热拷贝。正式备份仍由服务器自身的 Essentials `/backup` 或等价保存/冻结流程触发包装脚本，确保区块先落盘。EssentialsX `2.21.2` 会先执行 `save-all`、再执行 `save-off`，外部协调进程完成 VSS 快照交接后退出，随后由 Essentials 恢复 `save-on`。安装脚本只部署文件，不启动、停止或重启任何服务。

2026-07-27 的首次真实计划触发证明，单独依靠 `save-off` 仍不能让 Windows 外部进程读取 Java 已独占打开的 `.mca`：Lobby、Survival1 和 Survival2 都安全失败，未留下 ZIP 或 `.partial`。VSS 升级专门解决这一 Windows 文件句柄边界；它不改变地图半径、区块生成、服务端核心或游戏进程。

RCON 在三个 Paper 服务端均为关闭状态，因此不能用外部脚本伪造保存流程。

当前磁盘上的错峰计划为：

| 服务 | 计划时间 | 触发方式 |
| --- | --- | --- |
| Survival2 | 02:00 | Skript 调用 Essentials `/backup` |
| Survival1 | 04:00 | Skript 调用 Essentials `/backup` |
| Lobby | 05:30 | Skript 调用 Essentials `/backup` |

Lobby 的 Essentials 内建循环间隔已经从 `30` 改为 `0`，避免它与 Skript
重复触发。配置改动没有伴随热重载或服务器重启，当前 Java 进程和监听 PID
保持不变；这些时段在各服下一次正常重载后生效。仓库中的
`Configure-WorldBackupSchedule.ps1` 可重复部署三份计划，并在修改前备份现有文件，
但也不会自行重载或重启服务。

2026-07-26 复核的世界源文件量为：

- Survival1：`6,401,231,920` 字节。
- Survival2：`10,852,061,168` 字节。
- Lobby：`11,556,833` 字节。

空间预检按源文件量的 `102%` 再加安全余量计算。两台生存服仍共用 `E:`，
因此必须同时保证两份保留归档和下一份临时归档的空间，不得只看单台是否能通过。

## 5. 验收

旧引擎的本地和 VPS 临时夹具均连续执行两次备份，确认：

- 每次归档包含预期 2 个文件。
- 保留策略最终只留下 1 个 ZIP 和 1 个校验文件。
- SHA-256 与归档重新计算结果一致。
- 临时测试目录已经清理。
- 错峰配置脚本连续执行两次会生成独立回滚目录，第二次不改变结果。
- Essentials 配置缺少目标 `interval` 时，脚本会在修改任何计划文件前失败。

2026-07-27 又对正在运行且确实被 Java 锁住的
`E:\LobbyServer\world\entities\r.-1.-1.mca` 做了两层验证：

- 实时路径使用标准只读句柄会得到共享冲突，排除了“文件其实没有锁住”的假阳性。
- VSS 路径成功读取同一文件；候选引擎与部署后的生产引擎各完成一轮任务计划程序托管测试。
- 生产路径测试读取 `481` 个文件，生成 `3,352,747` 字节 ZIP，ZIP 条目数为 `481`。
- 生产测试 ZIP SHA-256 为 `06828D40BFC4F017CD0009916788A5E435E42BCBB97EF2FA5746B886414C7358`，旁车复核一致。
- 测试任务返回 `0`，`active.json` 与 VSS 卷影均自动清理；四个 Java PID 和启动时间前后完全一致。

以上证明 VSS 锁文件读取和后台完成链可用，但隔离测试没有冒充服务器内
`save-all`/`save-off` 的正式业务触发，因此生产世界归档状态仍保持“待验收”。

2026-07-27 22:47（Asia/Shanghai）再次只读核查时，`E:\backups` 与
`E:\backups-lobby` 的正式归档数仍为 `0`，`.partial`、`active.json`、状态 JSON
和残留 VSS 卷影均为 `0`。日志中的最后失败仍是 VSS 升级前读取 Java 锁定 `.mca`
的旧记录；四个 Java 进程都早于 VSS 引擎和新计划脚本部署。因此问题不是新引擎
再次失败，而是当前运行实例尚未加载磁盘上的新触发脚本。按既定边界不热重载、
不代替服主重启，下一验收点是服主下一次正常重启后的首个错峰计划窗口。
本次容量与状态快照见
[`evidence/INFRASTRUCTURE_CAPACITY_AND_WORLD_BACKUP_2026-07-27.json`](evidence/INFRASTRUCTURE_CAPACITY_AND_WORLD_BACKUP_2026-07-27.json)。

首次正式计划任务完成后还必须核对：

```powershell
Get-ChildItem E:\backups,E:\backups-lobby -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object FullName,Length,LastWriteTime

Get-Content C:\ProgramData\Hechao\WorldBackup\logs\*.log -Tail 100
```

对最新 ZIP 执行条目读取和 SHA-256 复核，并记录各盘剩余空间。以上是首次正式
验收前的历史门槛；2026-07-28 已完成，结果如下。

### 5.1 正式归档与异机副本

三个 Paper 服务端在玩家数为 `0` 时通过自身 Essentials
`save-all`/`save-off`、VSS 快照和后台压缩链生成正式归档。第一组归档已转存到管理机
`H:\server-backups\owl5\formal-world-acceptance\2026-07-28`：

| 服务 | 本地归档 | ZIP 字节 | 解压文件 | SHA-256 |
| --- | --- | ---: | ---: | --- |
| Lobby | `lobby-backup-20260728-040512.zip` | `3,352,746` | `481` | `0F49E9A3F40563124450AE5A1BA1357BA3477577585C6BD4D372431A455EC87E` |
| Survival1 | `survival1-backup-20260728-040003.zip` | `3,668,228,973` | `3,293` | `59BBD1307D54D03DD7194EC4A47E726C018DA4FAE7113DEECF507BA3F00B99F5` |
| Survival2 | `survival2-backup-20260728-041716.zip` | `4,946,045,967` | `6,573` | `470A37DEB34D596215EB4AD98B131A0701A39BC160642AE6EC989CEC78D1A626` |

三份文件在下载后重新计算哈希，全部 ZIP 条目均完整解压到独立目录，没有
`session.lock`。七个 `level.dat` 均可通过 GZip 解压，根标签均为
`TAG_Compound`。

区域文件验证采用确定性每 20 个抽 1 个的方式：总计 `9,049` 个 `.mca` 中检查
`454` 个，验证 `152,365` 个区块的位置表、扇区边界、重叠、长度与压缩类型，问题
为 `0`。这不是全量区域扫描，文档和证据不得把它扩大为 `9,049/9,049`。

### 5.2 当前远端保留

异机验收转存后曾留下两份状态指向已移走 ZIP，以及三个孤立 `.sha256`。2026-07-28
在玩家数为 `0` 时，Survival1 与 Lobby 重新通过完整 Essentials/VSS 业务链生成当前
远端归档；旧旁车先保存到 `E:\manual-backups\world-backup-state-repair-*`，再按精确
路径清理：

| 服务 | 当前远端归档 | ZIP 字节 | 条目 | SHA-256 |
| --- | --- | ---: | ---: | --- |
| Lobby | `E:\backups-lobby\lobby-backup-20260728-073926.zip` | `3,355,990` | `481` | `F97108099068ECAD736F69C8724DF31A0CF0AC7C55FEEB662396B9267A95A987` |
| Survival1 | `E:\backups\survival1-backup-20260728-073609.zip` | `3,668,231,421` | `3,293` | `3007759BA9F7E84799BD146B060FFC7DF1FA3B1EE7553FD47AB12725CB4F53AF` |
| Survival2 | `E:\backups\survival2-backup-20260728-041716.zip` | `4,946,045,967` | `6,573` | `470A37DEB34D596215EB4AD98B131A0701A39BC160642AE6EC989CEC78D1A626` |

三份状态均为 `Completed`，归档路径、重新计算哈希、条目数和旁车一致。
`active.json`、`.partial`、孤立旁车和本引擎拥有的 VSS 卷影均为 `0`；`E:` 剩余
`13,684,019,200` 字节。本轮没有停止或重启服务端。

机器可读证据见
[`evidence/WORLD_BACKUP_FORMAL_ACCEPTANCE_2026-07-28.json`](evidence/WORLD_BACKUP_FORMAL_ACCEPTANCE_2026-07-28.json)，
恢复验证器见
[`../tools/backup/Test-MinecraftWorldRestore.ps1`](../tools/backup/Test-MinecraftWorldRestore.ps1)。

## 6. 恢复

生产恢复前先停止对应后端，额外保存当前世界，验证目标归档 SHA-256，并恢复到独立临时目录。核对 `level.dat`、维度目录和关键插件数据后，才允许在维护窗口切换世界目录。不得把 ZIP 直接覆盖到运行中的世界。

2026-07-28 的演练只恢复到管理机隔离目录，没有覆盖或替换生产世界。这已经验证
归档可读取、完整解压和关键结构，但不等于执行过一次生产世界切换。

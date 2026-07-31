# 服控代理 0.2.0 正式发布

- 正式标签：`server-control-agent-v0.2.0`
- 制品源码提交：`088ca911abcceba741c45f3fef0296439a350d14`
- 产品版本：`0.2.0+088ca911abcceba741c45f3fef0296439a350d14`
- 部署目标：owl5、owl9

## 变更

- 每个目标显式声明一个 JVM 内存文件：`start.bat`、`start.ps1` 或 `user_jvm_args.txt`。
- 心跳读取并上报当前 `Xms`、`Xmx` 和代理配置的单服硬上限。
- 应用设置时要求文件中恰好存在一个 `-Xms` 和一个 `-Xmx`。
- 只接受最小 `512 MiB`、步长 `256 MiB`、`Xms <= Xmx <= maximumAllowedMemoryMiB` 的配置。
- `server.properties` 与内存文件作为同一事务处理：先备份、同卷临时替换；任一失败时恢复两个文件的原始字节。
- 安装脚本在启用代理前预检内存文件、参数唯一性和硬上限。

## 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.2.0-20260731T061127Z-win-x64.zip` | `33,137,565` | `3A6059E5A183187E85ECBA282472535D7253266ACF945ABA0821681777F9CF9F` |
| `Hechao.ServerControlAgent.exe` | `73,893,582` | `11CC411AECC1DFDA276FC4CD23E7653A13C3323C3DF495B1C1AD0B81FFBCC3BD` |

制品不包含 PDB、令牌、DPAPI 密文或生产配置。

## 生产部署

- owl5 任务：`Running`
- owl9 任务：`Running`
- 两台生产 EXE 的大小、SHA-256 和 ProductVersion 均与制品一致。
- owl5 回滚备份：`C:\ProgramData\Hechao\backups\server-control-agent-20260731T061646Z`
- owl9 回滚备份：`C:\ProgramData\Hechao\backups\server-control-agent-20260731T061915Z`
- 本次发布暂存目录已清理，回滚备份保留。

部署前后 Java PID 集合保持不变：

- owl5：`2576`、`6008`、`6112`、`7748`、`9428`、`10412`
- owl9：`2912`

其中受管且运行中的目标 PID 为 owl5 的 `2576`、`6008`、`6112`、`9428` 与 owl9 的 `2912`。未启动、停止或重启 Minecraft。

## 验证

- 服控代理测试：`19/19`
- 完整解决方案：`467/467`
- 九个生产文件均存在且各自只含一个 `-Xms`、一个 `-Xmx`。
- 两台代理持续上报新鲜心跳，九个目标均带完整内存字段。
- API 对代理上报进行独立边界验证，客户端不能直接调用代理。

操作与回滚边界见 [`SERVER_CONTROL_AGENT_OPERATIONS.md`](SERVER_CONTROL_AGENT_OPERATIONS.md)，生产证据见 [`evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_ACCEPTANCE_2026-07-31.json`](evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_ACCEPTANCE_2026-07-31.json)。

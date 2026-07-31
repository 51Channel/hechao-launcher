# ServerControlAgent 0.2.3 正式发布

- 正式标签：`server-control-agent-v0.2.3`
- 制品源码提交：`3916a86f408a15cefc89cbfce85e3fb2df992bd6`
- 产品版本：`0.2.3+3916a86f408a15cefc89cbfce85e3fb2df992bd6`
- 生产范围：owl5；owl9 保持 `0.2.1`

## 修复范围

owl5 的 Activity NeoForge 在空服暂停后无法处理 `save-all flush` 和 `stop`。线程栈确认
Minecraft 主线程阻塞在 `TerminalConsoleAppender -> FileOutputStream.write`。受管计划任务
继承了无人持续消费的 stdout 管道，管道写满后 Java 无法继续处理控制台输入；QuickEdit
不是本次永久阻塞的最终原因。

`0.2.3` 完成以下修复：

- 受管启动把 stdout/stderr 直接追加到
  `C:\ProgramData\Hechao\ServerControlAgent\logs\<serverId>-console.log`；
- 日志达到 `64 MiB` 时保留一份 `previous`，避免无限增长；
- 使用 `.NET ProcessStartInfo` 启动批处理并保留真实退出码；
- 受管启动与控制台桥关闭 QuickEdit，作为控制台冻结的附加防护；
- 世界备份子进程固定使用当前 PowerShell 7，而不是不存在的
  `$PSHOME\powershell.exe`。

## 正式制品

| 制品 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.2.3-20260731T162812Z-win-x64.zip` | `33,142,794` | `DCFCB19AE8F3301111E9283FE7C2E24B8A1F6E6746FC944003BD44686E9D27E0` |
| `Hechao.ServerControlAgent.exe` | `73,899,214` | `633A9C7EB63D982E2E9A0AC450E54679E74DBE4BD21DD38EEAFF6A572F9647F1` |

ZIP 只有一个 EXE，不包含 PDB、配置、令牌或凭据；归档内 EXE 哈希与正式构建一致。

## 生产部署

owl5 已从未打标签的中间版本 `0.2.2` 原子升级到 `0.2.3`：

- 代理 PID：`9556 -> 10108`；
- 回滚目录：
  `C:\ProgramData\Hechao\backups\server-control-agent-20260731T162452Z`；
- `Run-MinecraftServer.ps1` SHA-256：
  `498CF88BE7AE79714A5DBF7108E70BDA7C76366B6617CB9DD2E4F3ED5450C64A`；
- `Send-MinecraftConsoleCommand.ps1` SHA-256：
  `DDAAFA9672B9387B30DAF64D06690C2BCD4EA260C66241424DF983533FA197B5`；
- 正式世界备份脚本 SHA-256：
  `39489B3021A0E02969874AF9F3C167FD6B4F582D07F93E9E5967F6B2C3AABA0A`。

升级前后 Activity 任务均为 `Ready`，`25568` 无监听，运行标记不存在。其余 5 个
Java PID、启动时间和路径逐项一致；部署没有启动、停止或重启任何 Minecraft 服务端。

## 验证

- 受管启动回归探针：`4/4`；
- ServerControlAgent：`24/24`；
- Backup：`12/12`；
- 完整解决方案：`496/496`；
- 4 份 PowerShell 脚本 AST 检查通过；
- ZIP 单入口、大小和归档内 EXE SHA-256 复核通过；
- 生产部署后二次检查确认代理仍运行，临时上传文件为 `0`。

按照手动开服边界，本次没有为了验收重新启动 Activity。下次管理员手动启动时会自动
使用新的受管启动脚本；真实服首次启动后的日志持续写入和再次优雅关服仍应纳入当次运维
观察，但不需要修改现有服务端配置。

机器可读证据见
[`evidence/SERVER_CONTROL_AGENT_0.2.3_PRODUCTION_DEPLOYMENT_2026-07-31.json`](evidence/SERVER_CONTROL_AGENT_0.2.3_PRODUCTION_DEPLOYMENT_2026-07-31.json)。

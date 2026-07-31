# 服控代理 0.2.1 发布记录

- 正式标签：`server-control-agent-v0.2.1`
- 制品源码提交：`73afd07363ba2f55e917e42a50444cdd5107917a`
- 产品版本：`0.2.1+73afd07363ba2f55e917e42a50444cdd5107917a`
- 生产部署时间：2026-07-31 20:06 至 20:07 CST
- 部署目标：owl5、owl9

## 1. 修复范围

代理原先在同一个工作循环中依次发送心跳和执行命令。停止脚本持续数分钟时，整台 VPS 的所有目标都会停止上报并被 API 判定为“服控失联”。`0.2.1` 将心跳和命令拆为两个独立异步循环，长命令不再占用心跳调度。

本版没有扩大命令类型、控制台白名单、文件写入范围或进程权限。owl9 的 `pvp` 仍代表恐怖整蛊，`pvp-purpur` 仍代表真正 PVP。

## 2. 不可变制品

| 制品 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.2.1-20260731T120244Z-win-x64.zip` | `33,140,634` | `DBF3B48695C60139A5631A52491CA017A7B0382807D03E5DAFE838A7A7239DF0` |
| `Hechao.ServerControlAgent.exe` | `73,899,214` | `2D7D334C2205EB5F5D4032586B040F3624A85FA4B711630F151E5C8067D5C700` |

ZIP 只包含 `Hechao.ServerControlAgent.exe`。发布目录中的 PDB 未进入 ZIP，制品不包含配置、令牌、DPAPI 数据或其他凭据。

## 3. 生产部署

- owl5：代理 PID `3496 -> 4372`，回滚备份为 `C:\ProgramData\Hechao\backups\server-control-agent-20260731T120614Z`；Java PID `2576`、`6008`、`6112`、`7748`、`9428`、`10412` 及启动时间全部不变。
- owl9：代理 PID `5572 -> 2392`，回滚备份为 `C:\ProgramData\Hechao\backups\server-control-agent-20260731T120725Z`；真正 PVP Java PID `2912` 及启动时间不变。
- 两台 `Hechao Launcher Server Control Agent` 计划任务均为 `Running`，部署后代理错误日志均为 `0`。
- 只重启服控代理任务，没有启动、停止或重启任何 Minecraft 服务端。

## 4. 验收

- 代理测试：`20/20`。
- 完整解决方案串行测试：`480/480`。
- 生产 API 中两台代理均上报版本 `0.2.1`，9 个目标完整，运行目标为 `5` 个。
- 20 秒连续采样中两台代理心跳均多次推进，最大观测间隔为 owl5 `10.1` 秒、owl9 `7.2` 秒。
- 待处理服控操作和命令均为 `0`。
- 公开目录保持 `activity=Online`、`pvp=Closed`、`dollnight=Maintenance`，目录状态与实际物理服一致。

生产没有使用真实游戏服执行破坏性长命令；长命令隔离由独立调度测试、代码边界、生产持续心跳和 PID 不变共同验收。结构化证据见 [`evidence/SERVER_CONTROL_AGENT_0.2.1_PRODUCTION_DEPLOYMENT_2026-07-31.json`](evidence/SERVER_CONTROL_AGENT_0.2.1_PRODUCTION_DEPLOYMENT_2026-07-31.json)。

## 5. 回滚

每台主机独立停止 `Hechao Launcher Server Control Agent` 计划任务，使用上述备份目录中的旧 EXE 覆盖当前文件，再启动代理任务并核对产品版本、心跳和 Java PID。回滚代理不需要也不得重启 Minecraft。

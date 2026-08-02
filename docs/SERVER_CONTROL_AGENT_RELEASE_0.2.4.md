# ServerControlAgent 0.2.4 正式发布

- 正式标签：`server-control-agent-v0.2.4`
- 制品源码提交：`b0b10140a3fb68b067987e2ddfc2f3b48ff682d5`
- 产品版本：`0.2.4+b0b10140a3fb68b067987e2ddfc2f3b48ff682d5`
- 生产范围：owl5；owl9 保持 `0.2.1`

## 修复范围

`0.2.3` 已解决 Minecraft stdout 管道堵塞，但代理自身仍有两个可用性缺口：本机日志
创建、轮换或追加失败会中断工作进程；心跳或命令轮询遇到未列入旧白名单的异常时，
对应循环也可能永久退出。`0.2.4` 完成以下修复：

- 代理日志改为 `WriteBestEffort`，目录、轮换或写入失败只丢弃当前日志，不终止代理；
- 心跳和命令轮询统一使用 `ResilientLoopRunner`，除正常取消外的异常均记录后按原间隔
  继续下一轮；
- 保留两个循环相互隔离、命令回执幂等和取消时立即退出的既有语义；
- 增加未知异常、日志路径不可写、单循环故障和取消行为的调度回归测试。

本轮没有修改任何服务器目标、计划任务定义、控制台白名单、启动脚本、停止脚本、
世界备份脚本或 Minecraft 配置。

## 正式制品

| 制品 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.2.4-20260802T093332Z-win-x64.zip` | `33,146,240` | `44F155DA30C8DFFF2A8B885FE7E2168C81566EBEBB69D2E7FAEC0815CC71BA90` |
| `Hechao.ServerControlAgent.exe` | `73,908,430` | `9BAE24B2B5A5491B7A926661D37B2BA806599C5164C0F83C1307B4D25449301E` |

ZIP 只有正式 EXE 一个条目，不包含 PDB、配置、令牌或凭据；归档内 EXE 哈希与 owl5
正式文件一致。

## 生产部署

owl5 已从 `0.2.3` 原子升级到 `0.2.4`：

- 代理计划任务：`Hechao Launcher Server Control Agent`，状态 `Running`；
- 代理 PID：`7436 -> 8848`；
- 正式文件：
  `C:\ProgramData\Hechao\ServerControlAgent\Hechao.ServerControlAgent.exe`；
- 就地回滚文件：
  `C:\ProgramData\Hechao\ServerControlAgent\Hechao.ServerControlAgent.0.2.3.rollback-before-0.2.4-20260802T093332Z.exe`。

部署前后的五个 owl5 Java PID 均为
`2576 / 6008 / 7748 / 9428 / 10412`。升级只替换并重新运行服控代理；没有启动、停止、
重启 Minecraft 服务端，也没有下发控制台或快捷设置命令。

## 验证

- ServerControlAgent：`26/26`；
- Backup：`12/12`；
- 完整解决方案：`578/578`；
- 正式 EXE 文件版本：`0.2.4.0`；
- owl5 心跳持续报告 `0.2.4`、`7` 个目标和 `3` 个运行实例；
- owl9 保持 `0.2.1`、`2` 个目标和 `1` 个运行实例；
- 九个目标均为新鲜心跳，发布以来服控操作、待处理操作均为 `0`；
- API `0.26.2` 保持 `active/running`、`NRestarts=0`，公网健康与就绪均为 `200`。

## 回滚

如果 owl5 代理失去心跳，先停止且仅停止 `Hechao Launcher Server Control Agent` 计划
任务，再将就地回滚文件恢复为正式 EXE，最后重新运行同一代理任务。回滚前后必须记录
五个 Java PID 并确认完全一致；不得借代理回滚启动、停止或重启 Minecraft。

机器可读证据见
[`evidence/SERVER_CONTROL_AGENT_0.2.4_PRODUCTION_DEPLOYMENT_2026-08-02.json`](evidence/SERVER_CONTROL_AGENT_0.2.4_PRODUCTION_DEPLOYMENT_2026-08-02.json)。

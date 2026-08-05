# ServerControlAgent 0.4.0 正式发布

- 制品源码提交：`8423af7c451ad163c138d9de62954cfb43c4bd23`
- 正式标签：`server-control-agent-v0.4.0`
- owl5 部署时间：2026-08-06 04:25:52 CST
- owl9 部署时间：2026-08-06 04:27:07 CST

## 功能与边界

- 新增默认关闭的 `serverDeletionEnabled`；
- 只接受结构化 `DeleteServerFiles` 命令，不接受任意路径；
- 删除前两次确认受管 Java 进程已停止；
- 拒绝磁盘根目录、重解析点、代理状态目录和与其他受管服务端重叠的目录；
- 先在同卷原子移出运行路径，再递归清理且不跟随重解析点；
- 文件占用时保持运行路径已移除，通过心跳报告并持续重试；
- 命令重放和目录已不存在状态保持幂等；
- 已删除目标不会阻断后续代理升级，重新部署目录后恢复完整文件校验。

删除不影响外置备份、OSS 客户端、代理配置、计划任务或后台目标记录，也不会自动启停
Minecraft 或 Velocity。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.4.0-20260805T201046Z-win-x64.zip` | 33,219,561 字节 | `24A9EC3775D0517B823C754FF2F37534C5088DF0F7EE0575F198AC9A1653A271` |
| `Hechao.ServerControlAgent.exe` | 74,109,535 字节 | `088BDC8DA9AE3EF0FBE2F103FC2EB026A1A7A7A6BFDB657E8C3819A2BE7442F8` |

EXE 产品版本为
`0.4.0+8423af7c451ad163c138d9de62954cfb43c4bd23`。

## 生产能力基线

- owl5 开放：`dollnight`、`activity`、`fanstreet`、`yugong`；
- owl9 开放：历史 ID `pvp`，实际为恐怖整蛊服；
- 保持禁止：`lobby`、`survival1`、`survival2`、`pvp-purpur`；
- `pvp-purpur` 是真正的长期 PVP，不能与历史 `pvp` 混用。

真实生产配置是在两台主机现有配置上只合并上述布尔能力，没有用仓库模板覆盖生产漂移。

## 验证与回滚

- ServerControlAgent `51/51`，完整解决方案 `666/666`；
- 两台计划任务均为 `Running`，心跳版本均为 `0.4.0`；
- owl5 7 个目标、4 个能力位；owl9 2 个目标、1 个能力位；
- 9 个运行目录均存在，清理残留为 `0`；
- owl5 Java PID `2576/6008/7748/9428/10412` 和 owl9 PID `2912` 的启动时间与路径不变；
- 未启动、停止、重启或控制任何 Minecraft/Velocity 进程；未执行真实删除。

回滚备份：

- owl5：`C:\ProgramData\Hechao\backups\server-control-agent-20260805T202552Z`；
- owl9：`C:\ProgramData\Hechao\backups\server-control-agent-20260805T202707Z`。

代理可独立回滚，API 会在旧代理心跳关闭能力后隐藏按钮。若数据库已存在删除动作记录，
API 不得回滚到 `0.27.3`。

结构化证据见
[`evidence/SERVER_DIRECTORY_DELETION_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/SERVER_DIRECTORY_DELETION_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

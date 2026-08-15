# ServerControlAgent 0.6.0 正式发布

- 源码提交：`8d765dbd39e6fe138d140f53b99c22bc8323df8b`
- 正式标签：`server-control-agent-v0.6.0`
- 部署主机：owl5
- owl9 保持 `0.4.0`，本轮未操作

## 功能范围

- 新增结构化 `CreateDeploymentSlot`，只从固定 `activity` 模板派生 `activity-*` 动态槽；
- 每个槽使用独立 `E:\HechaoActivitySlots\<serverId>` 目录、主机固定文件快照和
  `Hechao-Server-<serverId>` 无触发器计划任务；
- 状态原子保存到代理状态目录，代理重启后重新验证；创建失败或取消会清理本轮目录、任务、
  快照和状态；
- 已有目录、已有任务、重解析点、越界路径和任意命令均被拒绝；
- 动态槽在有效部署标记出现前返回 `DEPLOYMENT_REQUIRED`，不会占用共享活动入口。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.6.0-20260815T024200Z-win-x64.zip` | 33,254,099 字节 | `25A31F9439FCB7518F8B068ADB18857143B127FF0437E278E17AFE10048207E8` |
| `Hechao.ServerControlAgent.exe` | 74,187,359 字节 | `85BA3C4034001166354E905DA1FFF50FE7456860A6D9870157A458DF8C0F62E2` |

ZIP 只包含单个 EXE，产品版本为
`0.6.0+8d765dbd39e6fe138d140f53b99c22bc8323df8b`，不含配置、凭据或 PDB。

## 测试与部署

- ServerControlAgent `64/64`、完整解决方案 `765/765`、PowerShell 7 脚本解析 `47/47`；
- 代理只在 owl5 升级，计划任务保持 `Running`，部署后进程 PID 为 `3548`；
- canonical runner SHA-256 为
  `9A9BADC0CD5883F3944A76BF48A26CC6D738DC933FBB908D4CA3C93584E67D0A`；
- 生产配置以现场配置为基底，只新增 `deploymentSlotProvisioning`：根目录
  `E:\HechaoActivitySlots`、模板 `activity`、上限 `12`；
- 显式回滚目录为
  `C:\ProgramData\Hechao\backups\server-control-agent-pre-0.6.0-20260815T024500Z`，
  安装器自动备份为
  `C:\ProgramData\Hechao\Backups\server-control-agent-20260815T024448Z`。

## 生产验收

- API `0.31.0` 持续收到 owl5 `0.6.0` 心跳，七个既有目标的端口、冲突组和在线状态正常；
- 升级前后 Java PID `7328`、`3652`、`3020`、启动时间和路径不变；其中活动服继续由
  PID `3652` 监听 `127.0.0.1:25568`，它在本次升级前已经运行；
- 动态槽根目录和状态文件仍不存在，因为尚未创建真实槽；固定 `activity` 不受影响；
- 本次只重启服控代理计划任务，没有启动、停止、重启或切换 Minecraft、Velocity。

## 回滚

在 API 没有待处理服控命令、没有 `Provisioning` 动态槽且已复核 Java 身份时，只停止
`Hechao Launcher Server Control Agent` 计划任务，从显式回滚目录恢复 EXE、配置、
canonical runner 和任务安装器，再启动代理并核对心跳。不得通过回滚删除动态槽目录、
数据库审计或控制任何 Minecraft 服务。

结构化证据见
[`evidence/SERVER_CONTROL_AGENT_0.6.0_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/SERVER_CONTROL_AGENT_0.6.0_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

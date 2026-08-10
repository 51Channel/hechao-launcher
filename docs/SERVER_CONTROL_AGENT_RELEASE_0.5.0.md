# ServerControlAgent 0.5.0 正式发布

- 二进制源码提交：`360c0fc83c77d465c6b521b6713ee71f644b7432`
- 安装器兼容修复提交：`c00230c416f252e0235a9c8b1f48b2b55335f520`
- 正式标签：`server-control-agent-v0.5.0`
- 部署主机：owl5
- owl9 保持 `0.4.0`，本轮未操作

## 功能范围

- 从受控 `.hechao-deployment.json` 读取活动槽实际部署的 `importId`、`profileId` 和
  `version`，随心跳上报给 API；
- 目录、标记或字段无效时保持空身份，不根据目录名、最近操作或后台期望值猜测；
- API 以该身份校验当前企划，防止上一场整合包遗留时误开放下一场；
- 安装器兼容旧生产配置中省略的新可选字段，同时继续对固定活动槽、目录边界和部署权限
  做失败关闭校验。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.5.0-20260810T0730-win-x64.zip` | 33,236,632 字节 | `6A4959205DC5E9D9231480DF02A30F755C01C81EFDCF7D6B7589FC4651489809` |
| `Hechao.ServerControlAgent.exe` | 74,154,591 字节 | `0A35DD12414E81FF87F885C4AE6D6E2BEEADAEC0BC7B3455C135668462B5D4DA` |

ZIP 只包含单个 EXE。产品版本为
`0.5.0+360c0fc83c77d465c6b521b6713ee71f644b7432`，不含配置、令牌、DPAPI 数据或 PDB。

## 测试与部署

- ServerControlAgent `58/58`、完整解决方案 `.NET 719/719`；
- 旧版回滚目录为
  `C:\ProgramData\Hechao\backups\server-control-agent-20260809T235446Z`，其中 EXE 为
  `0.4.2+7ecfddffec8cdc3bd18eafdd588f4f1c7eedda39`，SHA-256
  `D29F6504B69CE01ABEE0F5323E1BEC9AEE044203AF9F7AC266B5CB24A19C19B6`；
- 升级只重启 `Hechao Launcher Server Control Agent` 计划任务。当前任务为 `Running`、
  代理进程数为 1，正式 EXE 版本与 SHA-256 均匹配；
- 升级前后 Java PID `2576/7748/7924/9428/10412`、启动时间和规范化可执行路径完全一致；
- 活动任务为 `Ready`，`127.0.0.1:25568` 无监听，`E:\ActivityNeoForge` 和活动运行标记
  均不存在，`serverAction=none`；
- 候选任务为空，上传目录已清理。本轮没有启动、停止、重启或切换任何 Minecraft 与
  Velocity 进程。

## 回滚

在 API 没有待处理 `DeployPackage` 命令、活动槽保持停止且已独立复核 Java 身份后，只停止
服控代理计划任务，从上述备份恢复 `0.4.2` EXE 和配置，再启动代理并核对心跳。回滚代理
不得启动、停止或重启 Minecraft；API 已产生企划或部署身份时应优先前滚修复。

结构化证据见
[`evidence/SERVER_CONTROL_AGENT_0.5.0_PRODUCTION_DEPLOYMENT_2026-08-10.json`](evidence/SERVER_CONTROL_AGENT_0.5.0_PRODUCTION_DEPLOYMENT_2026-08-10.json)。

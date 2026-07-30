# 服务端控制代理 0.1.0 候选发布记录

> 状态：本地候选已构建并验证；尚未部署到游戏 VPS
>
> 制品源码提交：`bbc497e87aa1ccb5be50b5d7f1be4169641fd9cf`
>
> 生产状态：不存在已启用的服控代理

## 1. 能力与边界

- 只认配置中列出的服务端、固定启动计划任务和固定控制台桥任务。
- 停服先发送 `save-all flush`，再发送 Minecraft `stop`。
- 只修改 `max-players`、`view-distance`、`simulation-distance`、`difficulty`
  和 `white-list`，写入前保留备份。
- 控制台命令同时受 API 与本机前缀白名单约束。
- 不开放 PowerShell、CMD、SSH、任意文件浏览或任意进程终止。
- 使用 DPAPI `LocalMachine` 保存代理令牌，命令回执支持幂等重试。
- 共享端口要求运行标记、启动任务 PID/时间和监听 Java 祖先归属一致；不明占用会
  失败关闭。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.1.0-win-x64-20260730T112557Z.zip` | `33,130,750` | `31406F930A94CD1E5C2FBDCD80E25119172A302E604D4C4F560BBA11C321CE79` |
| `Hechao.ServerControlAgent.exe` | `73,884,366` | `89791937DFC45739ACAF9C2F291B4D3D99118C405D5D310CC8233B7629157FBF` |

程序文件版本为 `0.1.0.0`，候选为 Windows x64 自包含单文件，无 PDB。

## 3. 验证

- 代理自动测试 `11/11` 通过。
- 完整解决方案 `446/446` 通过。
- 配置、DPAPI 令牌、回执、快捷设置、运行状态和安全拒绝均有测试。
- 候选程序在缺少配置时返回使用说明和退出码 `64`，不会猜测生产路径。

## 4. 部署门槛

该候选不能直接复制示例配置到生产。必须先只读确认每个服务器的真实目录、端口、
计划任务、日志、控制台桥和冲突组。owl9 的历史 `pvp` 恐怖整蛊服
`C:\mc\server` 与真正 PVP `E:\MinecraftServer` 是两个独立目标，但共享
`25565`，必须放入同一冲突组。

首轮只能使用专用无玩家目标验收。验收前 API 服控保持关闭；启用后仍需先验证代理
离线拒绝、错误二次确认、设置备份、命令白名单、冲突停止失败取消和不明端口占用
拒绝。详细顺序见
[`SERVER_CONTROL_AGENT_OPERATIONS.md`](SERVER_CONTROL_AGENT_OPERATIONS.md)。

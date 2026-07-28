# API 0.21.0 候选记录

> 状态：隔离验收通过，未部署生产。
>
> 源码提交：`86b9912ca56db42a1d509d282a2000cf643c4a90`
>
> 当前生产仍为：`0.20.2-20260727T225819Z`

## 1. 变更

- 迁移 018 为 `launcher.servers` 增加
  `allow_protocol_translation boolean NOT NULL DEFAULT false`。
- 管理后台可按目标服务器启用协议转换授权；迁移不会自动开放任何现有目标。
- 非首次转服仍先执行账号、停服、等级和单服规则，再检查来源档案与目标档案。
- 只有目标服开关启用时才跳过 Minecraft 版本一致性检查。
- Fabric、Forge 与 NeoForge 目标仍要求客户端档案匹配，协议转换开关不能绕过。

## 2. 候选制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.21.0-20260728T025512Z.tar.gz` | `45,574,718` | `5FFE55E5905B2BBB08B3564E7B16AE952B4B5DF26ABC99C4988334114B4EE3B9` |
| `Hechao.Api` | `104,448,051` | `4725BD6CB5556B8F8353F5C8D9F295E671B5110A797C93E6886BFC7C6D74239E` |

发布物为 `linux-x64` 自包含单文件及管理后台静态资源，不含 PDB、环境文件或凭据。

## 3. 自动与隔离验收

- 完整 `.NET` 测试 `368/368`，其中 API `178/178`。
- 远端 `bash -n` 通过；API 主机没有安装 ShellCheck，因此未声称执行 ShellCheck。
- 使用生产备份
  `/var/backups/hechao-unified-account/20260727T230119Z/launcher-database.dump`
  恢复独立临时数据库，备份 SHA-256 与生产发布记录一致，`pg_restore` 可读
  `177` 个目录项。
- 候选只监听 `127.0.0.1:18093`，迁移 018 只应用到临时数据库。
- 所有既有目标迁移后均为 `false`，空值为 `0`。
- PVP 到大厅默认返回 `MinecraftVersionMismatch`；只为大厅开启后返回 `Allowed`。
- 大厅到 PVP 仍返回 `MinecraftVersionMismatch`，证明开关按目标服生效。
- 即使临时为 Activity 开启协议转换，PVP 档案进入 NeoForge Activity 仍返回
  `ClientProfileMismatch`。
- 重置开关后 PVP 到大厅再次返回 `MinecraftVersionMismatch`。
- 候选日志错误数为 `0`。
- 首轮自动验收清理后，又由 `manage-protocol-translation-staging.sh` 重建同构持久
  隔离副本供真实会话使用；Authorizer 受限凭据探针返回预期 `PlayerNotLinked`，
  证明 API 认证链可达，但不等同于真实玩家授权或转服。
- 持久副本通过 `refresh-heartbeats` 只读复制生产数据库中 120 秒内新鲜且在线的
  `lobby` 和 `pvp` 心跳。`pvp` 是恐怖整蛊的历史目录 ID，对应
  `C:\mc\server`；真正 PVP `E:\MinecraftServer` 未启动、未访问。同步后匿名目录
  实测大厅和恐怖整蛊均为 `Online`，Activity 因未同步新鲜心跳保持 `Closed`。
- 候选 API 的目录新鲜窗口为 180 秒，120 秒同步门槛保留 60 秒失败关闭余量。
  心跳复制前后均执行生产发布、迁移和进程基线断言；任一必需心跳缺失、超过 120 秒或
  离线时失败关闭。生产数据库只读，隔离候选数据库是唯一写入目标。
- 真实登录期间由 transient systemd timer 每 20 秒执行一次相同刷新；候选 API
  `stop` 或隔离环境 `remove` 会先停止定时器。多轮实测连续成功 10 次、失败 0 次，
  `heartbeatTargetState` 持续为 `2|2|2`。
- `start-heartbeat-sync` 同时建立本次验收时间标记；`issue-grant` 要求授权身份在
  标记之后新建有效登录会话。标记为 `0600 root:root`，登录前调用被拒绝且未创建
  任何短时授权，避免把授权签给生产备份中的旧会话或另一个账号。

验收使用临时合成身份，不读取或记录真实玩家 UUID、名称、令牌或数据库凭据。机器可读
证据见
[`API_PROTOCOL_TRANSLATION_CANDIDATE_2026-07-28.json`](evidence/API_PROTOCOL_TRANSLATION_CANDIDATE_2026-07-28.json)。

## 4. 生产不变性

测试前后生产 API 软链、进程状态和 systemd 重启计数保持不变，生产数据库迁移均为
`17`。首轮自动测试结束后临时数据库、transient systemd 单元和工作目录均已删除；
随后重建的真实会话隔离副本只监听回环地址，测试结束后须单独清理。生产
ViaVersion/ViaBackwards 与目录开关没有启用。

## 5. 发布门槛

该候选只证明迁移和授权门槛在生产数据副本上正确，不证明真实协议转换。发布生产前
仍需使用正确 PVP 1.20.1 客户端连接回环隔离代理并完成 `/hub`，核对 UUID、皮肤、
权限、物品栏、命令、移动、重连和正常退出。真实会话通过前不发布 API `0.21.0`，
不启用生产 Via JAR，也不切换 Velocity `enforce`。隔离代理使用会话来源修复候选
Authorizer `0.3.1`，记录见
[`VELOCITY_AUTHORIZER_RELEASE_0.3.1_CANDIDATE.md`](VELOCITY_AUTHORIZER_RELEASE_0.3.1_CANDIDATE.md)。

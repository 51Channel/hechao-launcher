# ServerControlAgent 0.8.2 正式发布

- 发布日期：`2026-09-04`
- Agent 源码提交：`049344d7d2b68d8f21ef1bd1f26b3a9272e94dd1`
- 控制台桥修复提交：`1ab7671a64448c61c1148ccc0a3c1ad78c89a339`
- 正式标签：`server-control-agent-v0.8.2`
- 部署主机：owl5
- owl9：保持 `0.7.2`，本轮未操作

## 修复范围

owl5 重启后没有 Administrator 桌面会话。动态槽启动任务仍使用
`InteractiveToken`，因此 API 能成功调用计划任务，但任务不会创建 Java 进程，最终返回
`START_TIMEOUT`。`0.8.2` 将动态槽启动任务统一改为无密码 `S4U`，并在安装后校验真实
`LogonType`，使服务器在主机重启且无人登录时也能启动。

同一根因还会阻断 Minecraft 控制台桥。配套安装器已改为 `S4U`，保留原有本地计划任务
隔离、PID 校验、单行命令限制和审计回执，不允许 SSH 进程直接向游戏控制台写入。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.8.2-20260903T225950Z-win-x64.zip` | `33,255,805` 字节 | `66C15994C7FFB02EBD10C6918CD7E95829E699FE84548B5E88E842B984D830C1` |
| `Hechao.ServerControlAgent.exe` | `74,196,063` 字节 | `7E8DFB43089FD7F930AB709F2DC06EE61475FD51072D820A49111B978492DC24` |

产品版本为 `0.8.2+049344d7d2b68d8f21ef1bd1f26b3a9272e94dd1`。ZIP 只包含单文件
EXE，生产文件摘要与本地制品一致。

## 生产部署与验收

- Agent 旧版备份为
  `C:\ProgramData\Hechao\backups\server-control-agent-20260903T230221Z`；其中旧 EXE
  为 `0.8.1`，SHA-256 为
  `6A36645D82F3FF40BE020E6EE8E13ED43E657B84D54F4A3C252561E95331C402`；
- 动态任务迁移前备份为
  `C:\ProgramData\Hechao\backups\dynamic-task-s4u-pre-20260903T225355Z`，共 `6` 个文件、
  `12,934` 字节，清单 SHA-256 为
  `4E3A18D9874E658EDC874373DBAEF7244DF93FFA22E11F12A873F40C94C9F145`；
- `activity-survival`、`activity-modular-boss`、`minigame-commercial-street` 三个动态槽
  启动任务均已验证为 `S4U`；前两个保持停止，未被启动；
- 控制台桥迁移前备份为
  `C:\ProgramData\Hechao\backups\console-bridge-s4u-pre-20260903T231509Z`；安装器生产
  SHA-256 为 `3868F0945FBD8E3AA137936BF9861243A76E23D76257099621685665106BA073`；
- 控制台桥在无交互登录环境中完成 `street selftest` 和 `list`，任务结果 `0x0`，待处理
  队列为 `0`；玩法插件明确输出 `SELFTEST PASS`，在线人数返回 `0/24`；
- 商业街以 Java `8`、`Xms=1024 MiB`、`Xmx=6144 MiB` 启动，PID `2948` 在
  `127.0.0.1:25602` 单独监听；Minecraft 协议探针返回 `1.12.2 / 340 / 0/24`；
- Forge 日志出现一次 `Done (3.235s)`，启动至验收时错误、FATAL、崩溃和 OOM 均为 `0`；
- API 数据库已上报 `reported_online=true / PID 2948 / Agent 0.8.2`，owl5 `10/10`
  目标和 owl9 `2/2` 目标心跳新鲜；控制操作与命令活动队列均为 `0`；
- API systemd 单元保持 `active`、`NRestarts=0`。本轮没有重启 API、Velocity、Lobby、
  owl9 或其他游戏服。

修复前的 `START_TIMEOUT` 仍作为历史审计记录保留。后台当前运行状态来自最新 Agent 心跳，
不应篡改历史失败操作。

## 发布边界

商业街客户端仍只分配到 `Test=100% / r2`；Gray 与 Production 未分配。目录继续隐藏并保持
`Closed`。本次只修复无人登录时的服控启动和控制台桥，不代表 Forge 转发、语音 UDP、
深度指标、许可证、世界恢复、fresh grant 或 `2/3/5/20` 真人灰度已经完成。

## 回滚

若 `0.8.2` 出现回归，应先通过结构化服控正常停止受影响目标并确认端口释放，再恢复 Agent
备份和动态任务 XML。控制台桥可独立恢复上述备份中的脚本与任务 XML。回滚后必须复核任务
登录类型、Agent 心跳、端口、命令队列和 API 审计；不得删除历史失败记录。

结构化证据见
[`evidence/SERVER_CONTROL_AGENT_0.8.2_PRODUCTION_DEPLOYMENT_2026-09-04.json`](evidence/SERVER_CONTROL_AGENT_0.8.2_PRODUCTION_DEPLOYMENT_2026-09-04.json)。

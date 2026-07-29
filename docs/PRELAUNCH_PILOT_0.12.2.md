# 启动器 0.12.2 上线灰度清单

> 目标：验证“启动器唯一切服、大厅只作内部承载、全局单 Minecraft 进程”，并在
> 含 U+200C 的既有数据根目录下稳定启动 Activity NeoForge。
>
> 本清单必须在 API `0.22.0`、Authorizer `0.4.0` 与 Lobby Guard `0.1.0`
> 已部署并通过基础健康检查后执行。
>
> 当前状态：自动发布、原生目录修复、Activity 安装版真实进服、同/跨档案三轮
> 切换、启动器重启接管、正常/异常退出后的 fresh grant 恢复以及 API 不可达后的
> 故障关闭/恢复已完成；四级账号、离线/无权限、`enforce` 和多人项目仍需要真实
> 玩家执行。
>
> 2026-07-30 只读快照：Member `21` 个但 Minecraft 绑定为 `0`，Participant `0`，
> Collaborator `0`，Administrator `1` 个且已绑定。生产当前具备外部灰度条件，但在
> 三个缺失等级取得合法正版身份并完成逐级多人验收前，不得切换 `enforce`。

## 1. 发布前门槛

- [x] API `/healthz`、`/readyz`、目录、登录、论坛和 Sub2API 回归正常。
- [x] 数据库迁移为 `19`；Lobby 为 `Infrastructure`、不可见、不可授权、继续监控。
- [x] Velocity 只保留统一公网入口；`lobby` 仅指向 `127.0.0.1:25566` 的内部占位。
- [x] HubCommand、ViaVersion、ViaBackwards、后端 `/hub` 与默认玩家回退均已退出活动路径；NPC 不再作为支持的切服入口。
- [x] Lobby Guard 已加载；大厅仅回环监听、空白名单，当前玩家连接数为 `0`。
- [x] 启动器 `0.12.2` 安装包 SHA-256 与正式发布记录一致，私有 OSS 匿名 `403`、两次签名回读 `200`。
- [x] Activity 工作目录、受管 Java 和五个原生目录属性全部使用不含格式字符的安全路径。
- [x] 安装版 `0.12.2` 完成正版会话、连接 Activity、进入世界、正常退出码 `0` 和零残留进程验收。

## 2. 单账号功能灰度

每个步骤记录时间、账号等级、目标服、档案版本、结果和对应审计事件，不记录令牌。

- [x] 使用已绑定的真实管理员账号登录赫朝账号并取得 Microsoft/Minecraft 正版会话。
- [x] 玩家目录中看不到 Lobby；管理员内部状态仍能看到 Lobby 心跳和指标。
- [x] 选择基础 Paper 服，直接进入授权目标，不出现大厅画面或大厅登录事件。
- [x] 选择 Activity，自动使用受管 Java 21、独立 `.minecraft` 和安全原生库目录，完成进入目标服与正常退出。
- [x] 同档案连续执行三轮 fresh grant 重进；每轮均验证旧进程完全退出后才启动新进程。
- [x] 跨档案换服连续执行三轮；`4,261` 个 200 ms 采样中 Minecraft Java 进程峰值为 `1`。
- [x] 启动器关闭再打开后能重新附着仍在运行的游戏，并正确恢复目标档案。
- [x] 目标服维护或关闭时主操作禁用；已有 Activity 进程保持不变，不创建第二个游戏进程，也不回退到大厅。
- [ ] 目标服离线或当前账号无权限时不启动游戏，也不回退到大厅。
- [x] API 暂时不可用时首次连接硬拒绝；恢复后重新申请授权可正常直达目标。隔离进程显示访客且没有进服动作、Java 进程或运行状态；恢复正常 API 后原会话自动恢复，Activity 再次进入世界并以退出码 `0` 结束。
- [x] Lobby 只监听 `127.0.0.1:25566` 且公网端口不可达；Lobby Guard 与空白名单保持生效。
- [x] 游戏正常退出与异常退出状态、诊断包和运行遥测均正确。

本节实机会话证据见
[`evidence/LAUNCHER_SWITCHING_REAL_ACCOUNT_2026-07-30.json`](evidence/LAUNCHER_SWITCHING_REAL_ACCOUNT_2026-07-30.json)
和
[`evidence/ACTIVITY_NEOFORGE_NATIVE_PATH_RECOVERY_2026-07-30.json`](evidence/ACTIVITY_NEOFORGE_NATIVE_PATH_RECOVERY_2026-07-30.json)。
API 故障关闭和恢复证据见
[`evidence/LAUNCHER_API_FAILURE_RECOVERY_2026-07-30.json`](evidence/LAUNCHER_API_FAILURE_RECOVERY_2026-07-30.json)。

## 3. 权限与人数灰度

- [ ] Member、Participant、Collaborator、Administrator 四级真实账号分别验证目录可见性和进服授权。
- [ ] 单服 Allow、Deny、维护状态、过期授权、重复授权和未知目标均符合预期。
- [ ] 依次完成 `2`、`3`、`5`、`20` 人灰度；观察 TPS、MSPT、GC、API 延迟和告警。
- [ ] 灰度期间大厅在线玩家始终为 `0`，但 LuckPerms 等级代理、指标、告警和备份正常。

当前账号数量、API 健康、Authorizer/Lobby Guard 哈希与大厅隔离快照见
[`evidence/EXTERNAL_GRAY_READINESS_2026-07-30.json`](evidence/EXTERNAL_GRAY_READINESS_2026-07-30.json)。

## 4. 通过与回滚

全部项目通过后，才允许把 Authorizer 从 `monitor` 切到 `enforce`，随后启用目录强制
登录。任一阶段失败时停止扩大人数，保留脱敏诊断和审计，按组件回滚；回滚不能移除
Lobby Guard、回环监听、空白名单或 API 的基础设施角色。

Activity 再次出现模组发现停滞、`UnsatisfiedLinkError`、原生目录属性分歧或路径
包含 U+200C 时，立即停止该档案灰度并保留 `latest.log` 与玩家确认的诊断包，不应
改回整游戏目录 junction。

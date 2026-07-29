# Velocity Authorizer 0.4.0 正式发布记录

> 状态：生产已安装，当前保持 `monitor`
>
> 正式标签：`velocity-authorizer-v0.4.0`
>
> 制品源码提交：`ba9576cd525de78fa639453e54466d967d5f1541`
>
> 直接回滚版本：`0.3.1`

## 1. 生产行为

- `lobby` 被登记为内部基础设施目标。
- 首次连接必须持有有效的一次性授权，并在后端登录前改写到授权玩家目标。
- API 超时、异常、拒绝、空响应、未知目标、内部目标、插件未初始化和损坏配置全部
  故障关闭。
- 后续转服到 `lobby` 永久拒绝；普通玩家目标在 `monitor` 下继续记录而不扩大权限。
- 游戏内 `/hub`、`/lobby`、NPC、Via 回程和代理默认回退不再承担转服。

## 2. 制品与部署

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoVelocityAuthorizer-0.4.0.jar` | `22,967` | `D3CEB0624A0AD70045897521795F275BC61973CF119873114149BDAEEAA95120` |

- 活动 JAR：`E:\Velocity\plugins\HechaoVelocityAuthorizer-0.4.0.jar`
- 部署备份：`E:\manual-backups\VelocityAuthorizer-0.4.0-20260729T150949Z`
- 旧 HubCommand、ViaVersion 和 ViaBackwards 备份：
  `E:\manual-backups\LegacyLobbyRouting-20260729T151223Z`
- 部署前已确认没有已建立玩家连接。
- 日志确认 `0.4.0`、`monitor`、`infrastructure-targets=lobby` 和监听器加载。

`velocity.toml` 中的 `try=["lobby"]` 仅为触发首次
`ServerPreConnectEvent` 的内部占位。Authorizer 必须在玩家登录任何后端前改写目标；
API 与 Lobby Guard 共同保证该占位不能成为玩家入口。

## 3. 验收与后续

- Java 自动测试 `26/26` 通过。
- API `0.22.0`、大厅基础设施角色、Lobby Guard 和旧回程移除均已生产落地。
- `enforce` 仍需普通、Participant、Collaborator、Administrator 四级真实账号和
  断线重连、API 短暂失败、目标下线验证后启用。

## 4. 回滚

回滚可恢复 `0.3.1` JAR 和原配置。回滚不得恢复 HubCommand、Via 回程或大厅玩家入口；
若 Authorizer 无法安全授权，应拒绝公网连接，而不是把玩家送入大厅。

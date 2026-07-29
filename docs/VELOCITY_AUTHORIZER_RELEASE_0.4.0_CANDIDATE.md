# Velocity Authorizer 0.4.0 候选发布记录

> 状态：代码、测试、JAR 和回滚安装器完成；未部署生产
>
> 直接回滚版本：`0.3.1`

## 1. 变更

- 增加 `infrastructure-targets=lobby`，可配置 1 至 32 个内部 Velocity 目标。
- Lobby 只允许作为首次授权改写发生前的内部占位；任何后续转服到 Lobby 都被拒绝。
- API 返回的 `serverId` 或 `velocityTarget` 若为内部目标，始终拒绝。
- 首次连接在以下任一情况都故障关闭，即使模式仍为 `monitor`：
  - API 超时、异常或空响应；
  - 授权拒绝；
  - 缺少或非法的服务器 ID/Velocity 目标；
  - 授权目标未注册；
  - 插件未初始化、配置损坏或模式为 `disabled`。
- 普通玩家目标的后续转服仍保留现有 `monitor` 观察语义；客户端版本/档案不兼容继续
  立即拒绝。
- 生产安装器会备份 JAR、插件配置和 `velocity.toml`，写入内部目标，启动失败时恢复
  旧 JAR 与配置。
- 旧 HubCommand、ViaVersion 和 ViaBackwards 由
  [`Disable-HechaoLegacyLobbyRouting.ps1`](../tools/server/Disable-HechaoLegacyLobbyRouting.ps1)
  备份后移出活动插件目录；Velocity 未恢复或 Authorizer 未加载时自动恢复旧 JAR。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoVelocityAuthorizer-0.4.0.jar` | `22,967` | `D3CEB0624A0AD70045897521795F275BC61973CF119873114149BDAEEAA95120` |

## 3. 自动验证

- Java 测试 `26/26` 通过。
- 覆盖首次 API 故障、空响应、授权拒绝、未知目标、内部目标、插件未初始化、合法目标改写、
  非首次 monitor 兼容行为和配置输入校验。
- 三份 Velocity 部署脚本通过 PowerShell 语法解析。

## 4. 生产门槛

- API `0.22.0` 先上线并确认 Lobby 不可授权。
- 替换前确认公网 Velocity 没有已建立玩家连接。
- 部署后日志必须确认 Authorizer `0.4.0` 初始化，首次无授权连接必须被拒绝。
- 删除 HubCommand、ViaVersion/ViaBackwards 回程层和所有默认回退。
- 真实账号完成 fresh grant、重复授权、API 超时和目标下线回归。

## 5. 回滚

安装器可恢复 `0.3.1` JAR 与原配置。回滚后仍必须保留 API 的基础设施角色和 Lobby
后端守卫；若 Authorizer 无法安全工作，公网入口应保持关闭或拒绝连接，不能回退到
Lobby 或任意玩家服。

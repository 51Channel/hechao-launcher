# 真实灰度与授权切换

> 生效日期：`2026-07-30`
>
> 当前生产：Velocity Authorizer `monitor`，目录强制登录关闭

本文档规定四级真实账号、多人灰度、Velocity `enforce` 和目录强制登录的唯一推进
顺序。大厅保留为内部前置能力承载器，但任何玩家都不能进入；玩家选择和切换服务器
只能通过赫朝启动器。

## 1. 当前边界

- 启动器、一次性进服授权、Velocity Authorizer、Lobby Guard、指标和告警均已部署。
- Administrator 有一个已绑定正版身份。
- Member、Participant、Collaborator 尚无已绑定正版身份。
- 在三个缺失等级各准备一个合法 Minecraft Java 身份前，不得切换 `enforce`。
- 不收集 Microsoft 密码、浏览器地址栏、访问令牌、玩家 UUID 或用户名作为 Git 证据。

## 2. 机器证据

`Test-HechaoGrayPilotReadiness.ps1` 的证据格式从 schema `2` 起同时记录：

- 每个等级的启用账号数和正版绑定数。
- 当前灰度窗口内成功消耗一次性授权的匿名去重人数。
- 每个 Velocity 目标的匿名授权人数。
- 指定拒绝原因是否真实发生。
- 在线人数、TPS、MSPT、GC、API 延迟、活动告警和大厅零玩家状态。
- Authorizer 的实际模式与本轮期望模式。

授权证据只保存等级、目标、原因和计数，不保存用户名、UUID、IP 或令牌。灰度工具
必须先启动，玩家再从启动器进入；已在服内的玩家不会被误算为本轮 fresh grant。

## 3. monitor 灰度

先运行空场 Readiness，再执行 `2`、`3` 和 `5` 人阶段。五人阶段必须覆盖四级账号及
切换前要求的拒绝路径：

```powershell
$tiers = @(
    "Member",
    "Participant",
    "Collaborator",
    "Administrator"
)
$denials = @(
    "LaunchGrantRequired",
    "InsufficientTier",
    "AccessDenied",
    "ServerUnavailable"
)

pwsh -NoLogo -NoProfile -File `
    .\tools\acceptance\Test-HechaoGrayPilotReadiness.ps1 `
    -Stage 5 `
    -Target activity `
    -ExpectedAuthorizerMode monitor `
    -RequiredAuthorizationTiers $tiers `
    -RequiredDeniedReasons $denials `
    -DurationSeconds 900 `
    -SampleIntervalSeconds 5 `
    -ApiHostName $apiHost `
    -ApiIdentityFile $apiKey `
    -ApiKnownHostsFile $apiKnownHosts `
    -VelocityHostName $velocityHost `
    -VelocityPort $velocitySshPort `
    -VelocityIdentityFile $velocityKey `
    -OutputPath $monitorEvidence
```

任何技术阻断、缺失等级、缺失授权、缺失拒绝路径、活动 Critical、大厅出现玩家、
TPS/MSPT/GC 或 API 延迟超阈值，都必须停止本档并保持现有生产模式。
`MinecraftVersionMismatch` 和 `ClientProfileMismatch` 继续由生产兼容矩阵覆盖；
游戏内转服已经取消，不要求玩家为了制造不存在的正常路径而绕过启动器。

## 4. enforce 闸门

先让独立闸门复核 monitor 证据：

```powershell
pwsh -NoLogo -NoProfile -File `
    .\tools\acceptance\Test-HechaoAuthorizerEnforceGate.ps1 `
    -EvidencePath $monitorEvidence
```

只有返回 `passed=true` 时，才允许在零连接维护窗口切换：

```powershell
pwsh -NoLogo -NoProfile -File `
    .\tools\server\Set-HechaoVelocityAuthorizerMode.ps1 `
    -HostName $velocityHost `
    -Port $velocitySshPort `
    -IdentityFile $velocityKey `
    -DesiredMode enforce `
    -PreflightEvidencePath $monitorEvidence `
    -Apply `
    -OutputPath $enforceChangeEvidence
```

不带 `-Apply` 时只做生产只读检查。应用时工具会：

1. 再次核对 Lobby 回环监听、空白名单和基础设施目标。
2. 拒绝存在玩家连接的重启窗口。
3. 把 Authorizer 配置备份到 `E:\manual-backups` 并写入 SHA-256。
4. 只重启 `Codex-Velocity-Live`，不重启任何 Minecraft 后端。
5. 校验监听和初始化日志。
6. 任一步失败时恢复原配置并把 Velocity 恢复到原模式。

## 5. enforce 复验与目录强制登录

切换后必须重新执行至少五人阶段，把
`-ExpectedAuthorizerMode` 改为 `enforce`，其余四级账号、拒绝路径、性能和大厅要求
不变。随后用同一个闸门复核 enforce 证据：

```powershell
pwsh -NoLogo -NoProfile -File `
    .\tools\acceptance\Test-HechaoAuthorizerEnforceGate.ps1 `
    -EvidencePath $enforceEvidence `
    -ExpectedEvidenceAuthorizerMode enforce
```

通过后才允许启用目录强制登录：

```powershell
pwsh -NoLogo -NoProfile -File `
    .\tools\server\Set-HechaoCatalogAuthentication.ps1 `
    -ApiHostName $apiHost `
    -ApiIdentityFile $apiKey `
    -ApiKnownHostsFile $apiKnownHosts `
    -DesiredState Enabled `
    -EnforceEvidencePath $enforceEvidence `
    -Apply `
    -OutputPath $catalogChangeEvidence
```

该工具默认只读。应用时会保护性备份 API 环境文件、只重启
`hechao-launcher-api.service`，并验证本机及公网 `healthz`、`readyz` 和匿名目录
`401`。失败时自动恢复环境文件并重启原版本；不会修改官网、中转 API、Velocity 或
游戏服。

## 6. 最终灰度

目录强制登录启用后继续执行 `20` 人阶段，并完成：

- 安装、覆盖升级、断点续传、修复和玩家主动回滚。
- 同档案重连及跨 Paper、Fabric、NeoForge 档案切换。
- 维护、关闭、等级不足、单服拒绝和 API 短暂不可用。
- 大厅全程零玩家，所有后端公网直连保持拒绝。
- TPS、MSPT、GC、API p95 和告警完整观察周期无阻断。

20 人阶段通过只代表当前规模通过，不自动证明 30 人以上容量。扩容必须另开一档证据，
且 owl9 低磁盘 Warning 未处理前不得把它忽略为“已验收”。

## 7. 当前结论

切换脚本、匿名授权证据和失败关闭闸门已经实现并完成生产只读冒烟。当前仍缺三个等级
的正版绑定和真实多人参与，因此正确动作是保持 `monitor` 与匿名目录 `200`，不执行
任何生产切换。

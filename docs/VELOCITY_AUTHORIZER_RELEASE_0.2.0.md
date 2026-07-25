# Velocity 授权插件 0.2.0 发布记录

> 部署时间：2026-07-26
> 模式：`monitor`
> 代理实例：`owl5-main`

## 变更

插件会读取 API `0.12.0` 返回的授权目标。在首次连接事件中，若启动授权选择的 `velocityTarget` 与代理初始目标不同，插件会把目标改写到已注册的后端；后续转服继续按实际目标校验。

未知目标、API 拒绝或 API 故障在 `monitor` 中放行并记录，在 `enforce` 中拒绝。此次部署没有切换强制模式。

## 生产基线

| 项目 | 值 |
| --- | --- |
| JAR | `E:\Velocity\plugins\HechaoVelocityAuthorizer-0.2.0.jar` |
| 大小 | `20,631` 字节 |
| SHA-256 | `9CBBB1453D7260CD8AAD48EDC6BE4E80B8A5E41374D5012E0DBA64ACC0188D37` |
| 配置 | `E:\Velocity\plugins\hechao-velocity-authorizer\config.properties` |
| 备份 | `E:\manual-backups\VelocityAuthorizer-0.2.0-20260726-044028` |
| 计划任务 | `Codex-Velocity-Live` |
| 当前监听 | `127.0.0.1:25577`，PID `6068` |

04:40:31 的启动日志确认插件 `0.2.0` 以 `monitor` 模式加载。公网入口 `mc.hehe11.fun:15156` TCP 可达；大厅、Survival1、Survival2 与活动后端没有随本次部署重启。

## 验收

- Java 测试：`11/11`
- 生产 API 合成授权：`lobby -> pvp`
- 返回等级：`Administrator`
- 返回 LuckPerms 主组：`owner`
- 临时授权数据已清理，审计保留

自动测试与合成授权证明路由决策和目标改写契约成立，但不替代真实玩家首次连接、NPC 转服、`/hub`、断线重连和 API 故障灰度。

## 回滚

备份目录包含旧 `0.1.0` JAR、配置和 `velocity.toml`。旧 JAR SHA-256 为 `BA1E02150714A34D5FCEA348C64C578B31D9E4C85B53D3DA8EFD3681F31388C4`。回滚时恢复旧文件并只重启 Velocity，不操作 Minecraft 后端或数据库。

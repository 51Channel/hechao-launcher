# Velocity 授权插件 0.3.0 发布记录

> 部署时间：2026-07-28
>
> 模式：`monitor`，客户端版本/档案不兼容仍立即拒绝
>
> 代理实例：`owl5-main`
>
> 源码提交：`c2b50e2ac75b8bc9a66cfcb9691c7ee566ebfd57`

## 1. 变更

- 每个玩家会话缓存首次启动授权返回的服务器 ID，并在后续转服请求中发送
  `sessionServerId`。
- 缓存保持为“启动本次 Minecraft 进程的档案来源”，不会在经过大厅时被覆盖。
- `MinecraftVersionMismatch` 与 `ClientProfileMismatch` 在 `disabled` 之外的
  `monitor`/`enforce` 模式均立即拒绝，避免客户端协议崩溃。
- 玩家断线时清理会话缓存。
- 初始授权缺少服务器 ID 时，`enforce` 故障关闭，`monitor` 记录告警。
- 部署脚本先检查在线连接和哈希，备份旧 JAR/配置，原子替换并从精确
  `logs\latest.log` 验证加载；失败会自动恢复旧 JAR。

## 2. 生产基线

| 项目 | 值 |
| --- | --- |
| JAR | `E:\Velocity\plugins\HechaoVelocityAuthorizer-0.3.0.jar` |
| 大小 | `21,152` 字节 |
| SHA-256 | `289B13472AEAC4073895EF9BE7E630B4B5AACEC48A4D0FD849BBAFE0064E681D` |
| 配置 | `E:\Velocity\plugins\hechao-velocity-authorizer\config.properties` |
| 备份 | `E:\manual-backups\VelocityAuthorizer-0.3.0-20260727T231243Z` |
| 计划任务 | `Codex-Velocity-Live` |
| 监听 | `[::]:25577`，PID `472` |
| 已建立连接 | `0` |

启动日志确认 `hechao-velocity-authorizer 0.3.0` 以 `monitor` 为
`owl5-main` 初始化，代理在 `0.98` 秒内完成启动。公网入口
`mc.hehe11.fun:15156` TCP 可达。

第一次部署验证错误地选择了压缩历史日志，候选其实已正常加载，但脚本按失败路径自动
恢复 `0.2.0` 并重启代理。修正为读取精确 `logs\latest.log` 后，第二次部署成功。
这同时真实验证了自动回滚路径。

大厅、Survival1、Survival2 和 Activity 的 PID 在两轮操作中均未改变。

## 3. 验收

- Java 测试：`13/13`
- 生产 API 客户端兼容矩阵：`8/8`
- 远端 JAR SHA-256 与本地制品一致
- 启用 JAR 数量：`1`
- Velocity 启动日志版本和初始化模式均匹配
- 部署前已建立玩家连接：`0`

真实正确客户端仍需分别安装 Activity NeoForge 与 PVP Fabric 档案，再验证首次目标、
返回大厅、再次进入、拒绝提示和断线重连。四级真实账号与多人灰度未由合成矩阵代替。

## 4. 回滚

回滚目录包含旧 `0.2.0` JAR、配置、`velocity.toml` 和校验清单。恢复后只重启
Velocity，不重启 Minecraft 后端。API 同步回滚到 `0.20.1` 后，应先保持
`monitor` 并复验真实连接，再决定是否继续灰度。

# Lobby Guard 0.1.0 正式发布记录

> 状态：生产运行中
>
> 正式标签：`lobby-guard-v0.1.0`
>
> 制品源码提交：`ba9576cd525de78fa639453e54466d967d5f1541`
>
> 适用服务端：`E:\LobbyServer`

## 1. 安全边界

大厅保留为 LuckPerms、状态指标、告警和世界备份等前置能力的内部承载器，但不是玩家
服务器。Lobby Guard 在 Paper 登录链路独立拒绝所有玩家，包括 OP 和管理员；它不
注册玩家命令，也不修改 LuckPerms、指标、告警、备份或计划任务。

## 2. 制品与部署

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoLobbyGuard-0.1.0.jar` | `3,047` | `B0B7AA651994797B16B1271D332EF03A218F8BB8FEC3226CF0F705D74311DE99` |

- 活动 JAR：`E:\LobbyServer\plugins\HechaoLobbyGuard-0.1.0.jar`
- 部署备份：`E:\manual-backups\LobbyGuard-0.1.0-20260729T151317Z`
- `server-ip=127.0.0.1`
- `white-list=true`
- `enforce-whitelist=true`
- 白名单为空
- 监听仅为 `127.0.0.1:25566`

部署日志确认 Lobby Guard、LuckPerms 等级代理和指标代理均加载；大厅保持零玩家连接，
心跳、TPS/MSPT/GC 和正式备份继续工作。

## 3. 验收与回滚

- Java 自动测试 `3/3` 通过。
- API 目录和授权已独立过滤大厅，Velocity 也永久拒绝内部目标。
- 真实四级账号旁路尝试仍纳入最终玩家灰度，用于验证从公网入口、旧命令和后端地址均
  无法进入大厅。

回滚只允许恢复守卫 JAR 的先前版本，不得恢复公网监听、非空白名单或玩家目录。任何
故障下都至少保留回环监听、强制空白名单和 API 基础设施角色。

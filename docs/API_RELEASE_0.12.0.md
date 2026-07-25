# 启动器 API 0.12.0 发布记录

> 发布 ID：`0.12.0-20260725T203001Z`
> 状态：生产在线
> 直接回滚目标：`0.11.3-20260725T195000Z`

## 变更

API 内部 Velocity 授权响应现在返回被消费启动授权对应的 `serverId` 与 `velocityTarget`。Velocity 插件 `0.2.0` 可在玩家首次连接统一入口时，把代理给出的初始大厅目标改写到启动器中选择的服务器。后续 NPC、命令或插件转服仍按实际目标重新授权。

## 发布物

| 项目 | 值 |
| --- | --- |
| 单文件大小 | `103,716,915` 字节 |
| 单文件 SHA-256 | `B46A22280243BA9801EB66FD628ED598CD27F0FED7995788C4452D222C3B27D1` |
| 归档 | `artifacts/releases/hechao-api-0.12.0-20260725T203001Z.tar.gz` |
| 归档大小 | `45,382,027` 字节 |
| 归档 SHA-256 | `C76DA133466A4D609F8009A5206FDAFCDDE72DC0CB7D78FBC8E8C8B473DA5D41` |

## 备份

- API 与配置：`/var/backups/hechao-launcher/api-predeploy/pre-api-0.11.3-20260725T203220Z.tar.gz`
- API 备份 SHA-256：`71D850AABD85AB203CE585C679A53609F91F013DFDAE6937E1B208E88625EC12`
- 数据库：`/var/backups/hechao-launcher/database/hechao-launcher-20260725T203227Z.dump`
- 数据库 SHA-256：`E1E3F1F864D1CB363E426346892DC0C6651409E001DA9F0B05F9435D55A5C7D9`

## 验收

- `/healthz` 与数据库感知的 `/readyz` 在本机和公网均返回 200。
- `hechao.world` 与 `api.hechao.world` 旧业务保持正常。
- 生产合成授权以初始目标 `lobby` 请求，返回 `Allowed=true`、`ServerId=pvp`、`VelocityTarget=pvp`、`AccessTier=Administrator`、`LuckPermsPrimaryGroup=owner`。
- 一次性授权已成功消费；临时授权行已删除，运维审计保留。
- 完整解决方案测试为 `200/200`，Velocity 测试为 `11/11`。

## 开关

生产继续保持：

```text
Authentication__EnforceCatalogAuthentication=false
Velocity mode=monitor
AdminWeb__Enabled=true
```

22 个社区账号中只有 1 个绑定 Minecraft，管理员 MFA 凭据数为 0。真实四级账号和 MFA 完成前，不得启用目录强制登录或 Velocity `enforce`。

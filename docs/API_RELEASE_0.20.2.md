# API 0.20.2 发布记录

> 状态：已生产部署并完成客户端兼容矩阵回归
>
> 当前生产：`0.20.2-20260727T225819Z`
>
> 源码提交：`c2b50e2ac75b8bc9a66cfcb9691c7ee566ebfd57`

## 1. 变更

- Velocity 后续转服请求新增 `sessionServerId`，表示本次 Minecraft 进程最初由哪个
  客户端档案启动。
- API 同时读取来源档案和目标服的 Minecraft 版本、加载器与客户端档案 ID。
- 不同 Minecraft 版本返回 `MinecraftVersionMismatch`。
- Forge、Fabric、NeoForge 目标要求来源和目标使用同一客户端档案；不一致时返回
  `ClientProfileMismatch`。
- 同版本 Paper/Vanilla 目标继续允许互转；模组客户端也可以返回同版本 Paper 大厅。
- 两个兼容性拒绝原因会由 Velocity `0.3.0` 在 `monitor` 模式下立即拦截，避免错误
  客户端进入模组服后才因协议解码失败退出。其他权限拒绝仍保持原有 monitor 行为。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.20.2-20260727T225819Z.tar.gz` | `45,574,298` | `AE5561DFA85FB59476C22D66CB2AF0781112345B82BED8E9D7825DBC34559B32` |
| `Hechao.Api` | `104,444,979` | `327D17A6F24833CDAD9F912AC16D87EC2DEE463F7DBD427B6E672307DA24A6F6` |

## 3. 自动验证

- `.NET` 完整解决方案 `360/360` 通过，其中 API `175/175`。
- 客户端兼容规则定向测试 `16/16` 通过。
- Velocity Java 测试 `13/13` 通过。
- 三个新增 PowerShell 验收/部署/恢复工具均通过语法解析。

## 4. 生产部署

切换前统一备份位于：

```text
/var/backups/hechao-unified-account/20260727T230119Z
```

- 备份清单 SHA-256：
  `2C9FDD49DCF30A0AE4C30AC770E6A2DFB928E0B87D14964522C335A12DB1024D`
- 数据库 dump SHA-256：
  `025061B836A02983FF0C376CD0D51A0760217B387F4E1ADB728864DDD2C6A6D8`
- `pg_restore --list` 可读取 `177` 个目录项。
- 当前链接指向
  `/opt/hechao-launcher-api/releases/0.20.2-20260727T225819Z`。
- 生产程序哈希与本地候选一致，`healthz`、`readyz` 和数据库均正常。
- systemd `NRestarts=0`，部署后 warning/error 为 `0`。
- `hechao.world`、`api.hechao.world`、启动器 API 和管理后台旧入口均完成回归。

## 5. 生产兼容矩阵

使用真实已绑定账号的匿名身份和生产内部接口执行了八条非首次连接判定：

| 会话档案来源 | 目标 | 结果 |
| --- | --- | --- |
| Lobby 1.21.11 | Survival2 1.21.11 Paper | `Allowed` |
| Lobby 1.21.11 | Survival1 1.21.11 Paper | `Allowed` |
| Lobby 1.21.11 | Activity 1.21.11 NeoForge | `ClientProfileMismatch` |
| Lobby 1.21.11 | PVP 1.20.1 Fabric | `MinecraftVersionMismatch` |
| Activity 1.21.11 NeoForge | Lobby 1.21.11 Paper | `Allowed` |
| Activity 1.21.11 NeoForge | Activity 1.21.11 NeoForge | `Allowed` |
| PVP 1.20.1 Fabric | PVP 1.20.1 Fabric | `Allowed` |
| PVP 1.20.1 Fabric | Lobby 1.21.11 Paper | `MinecraftVersionMismatch` |

结果为 `8/8`。脚本只从受限生产配置中读取内部凭据，不输出或保存凭据。

该矩阵证明生产 API 判定正确，但不替代正确赫朝客户端安装 Activity/PVP 档案后的真实
进服、`/hub`、NPC、断线重连和四级账号灰度。

机器可读证据见
[`evidence/API_VELOCITY_CLIENT_COMPATIBILITY_2026-07-28.json`](evidence/API_VELOCITY_CLIENT_COMPATIBILITY_2026-07-28.json)。

## 6. 回滚

API 直接回滚目标为 `0.20.1-20260727T145451Z`。回滚 API 时必须同时把 Velocity
插件退回 `0.2.0`，否则插件发送的会话来源字段虽然保持向后兼容，但将失去硬兼容
拦截。回滚前后都要复验八条矩阵，不操作 Minecraft 后端。

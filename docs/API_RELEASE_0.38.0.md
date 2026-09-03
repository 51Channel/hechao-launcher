# 赫朝启动器 API 0.38.0 正式发布

- 发布日期：`2026-09-03`
- 发布目录：`/opt/hechao-launcher-api/releases/0.38.0-20260903T084151Z`
- 制品源码提交：`4838bc303fad960db9f9b8822c6061b9c297932c`
- 正式标签：`api-v0.38.0`
- 配套 owl5 Agent：`0.8.1`
- 数据库迁移：保持 `35/35`，本版不新增迁移

## 功能范围

本版把整合包分析器确认的 Java 主版本传递到服务端部署命令：

- 后台直接部署和活动企划部署共用 `javaMajorVersion` 合同；
- API 只传递已确认的 Java 主版本，不选择或下发 VPS 上的 Java 路径；
- 字段保持可空，旧命令、旧租约和旧代理载荷仍可反序列化；
- 显式 Java 版本由目标 VPS 的受管 Agent 失败关闭选择，不能静默回退到默认 Java；
- 商业街 Forge `1.12.2` 因此以 Java `8` 标记部署，而现有 Java 21 目标行为不变。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.38.0-20260903T084151Z-linux-x64.tar.gz` | `46,999,748` 字节 | `F70ACFDAC99DE18D8EED1ECA0D5BD3B53ADF7191FD468CFB6CECD28BBD50A5DA` |
| `Hechao.Api` | `105,703,492` 字节 | `EB3897FD162B192916F40FA8E6C75CB1CA2620475A54E1FBE1066F8CB30533ED` |

本地归档、解包后的单文件和生产发布目录已逐项复算摘要。最近一份已验证数据库备份为
`/var/backups/hechao-launcher/database/hechao-launcher-20260902T194802Z.dump`，大小
`8,154,397` 字节，SHA-256 为
`82B3D0211203DCC7E36182A5FD9B457EBFFBF53A28C69CE3BC9F3E3996270299`。本版没有数据库
结构变更，也没有修改 API 环境文件。

## 生产验收

- API 专项 `383` 项通过，完整解决方案 `836` 项通过、`1` 项外部 PostgreSQL 条件测试
  按环境跳过；Release 构建 `0` 警告、`0` 错误；
- 当前链接指向上述不可变发布目录，生产单文件长度和 SHA-256 与本地制品一致；
- `hechao-launcher-api.service` 为 `active/running`，PID `1287130`、`NRestarts=0`；
- 回环 `/healthz`、`/readyz` 均为 `200`，进程启动后 warning 及以上日志为 `0`；
- 数据库为 `35/35`，整合包、服控命令、服控操作和活动部署活动队列均为 `0`；
- Publisher `1.2.1` 与 owl5 Agent `0.8.1` 心跳新鲜；商业街部署命令明确携带
  `javaMajorVersion=8`、`Xms=1024 MiB`、`Xmx=6144 MiB`；
- API 发布只重启 API systemd 服务，没有启动、停止、重启或发送命令给 Minecraft、
  Velocity、Publisher 或游戏 VPS 上的任何服务端。

## 回滚

程序故障时原子切回
`/opt/hechao-launcher-api/releases/0.37.0-20260823T182444Z`，只重启
`hechao-launcher-api.service`。已经存在 Java 8 部署标记时，回滚 API 不得同时把 owl5
Agent 降到 `0.7.2`；应保留 `0.8.x`，关闭新的整合包确认并向前修复。数据库、审计、
导入记录、客户端清单和不可变 OSS 对象均不得删除。

结构化证据见
[`evidence/API_0.38.0_PRODUCTION_DEPLOYMENT_2026-09-03.json`](evidence/API_0.38.0_PRODUCTION_DEPLOYMENT_2026-09-03.json)。

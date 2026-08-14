# API 0.30.3 正式发布

- 正式发布 ID：`0.30.3-20260814T072942Z`
- 制品源码提交：`e69cb9d93b71696d391999856fe7a2a86703161f`
- 正式标签：`api-v0.30.3`
- 生产切换时间：2026-08-14 16:13 CST
- 数据库迁移：无，保持 `028`

## 发布范围

本版本只收紧内部 LuckPerms 等级命令协议：

- `claim` 和 `complete` 必须携带合法代理版本及精确协议 `2`；
- 缺少字段的 `0.1.0`、`0.1.1`、`0.1.2` 请求在访问命令仓库前返回输入错误；
- `claimed_by` 使用 `agent-id@agent-version/protocol`，完成回执必须匹配领取时的软件版本
  和协议；
- 完成审计增加 `AgentVersion` 和 `ProtocolVersion`；
- 不修改管理后台玩家操作、身份映射、快照导入、Launcher、活动、分发或服控合同。

## 正式制品与测试

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.3-20260814T072942Z.tar.gz` | 46,246,724 字节 | `7EA2A288DBC8ED7C2AD51DD0C03F90125EFA011CB6AA2AB12878947C8685E3D4` |
| `Hechao.Api` | 105,245,124 字节 | `E155542923AF167BC85351306B16B655EC7D8598583173017C7288BAA45BC2DC` |

归档共 161 项，危险路径为 `0`；本地归档、正式 release 和运行二进制哈希一致。环境文件、
凭据、Cookie、玩家标识和签名 URL 未进入制品。API `311/311`、完整解决方案 `731/731`，
旧 JSON 载荷、协议版本、租约身份和完成回执测试均通过。

## 备份与部署

- PostgreSQL custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.30.3-20260814T080918Z.dump`，
  6,171,924 字节，SHA-256
  `657FF2AFC30930B1ECA6F09AD2FC1D09D8C0039627DEAEB2171E5695A0072DA2`；
  `pg_restore --list` 返回 239 行；
- API 环境与旧 release 快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.3-20260814T080918Z.tar.gz`，
  46,138,782 字节，SHA-256
  `EAAB721517AE84DEAEFEC625BFC6A6BEAC7BBECEF92F4B3A6EACAA7EBFF29319`；
- 原子切换到 `/opt/hechao-launcher-api/releases/0.30.3-20260814T072942Z` 后只重启
  `hechao-launcher-api.service`。Nginx、Publisher、Velocity、Minecraft 和
  ServerControlAgent 均未操作。

## 生产验收

- API 为 `active/running`，PID `80904`、`NRestarts=0`，只监听
  `127.0.0.1:8090`；回环和公网 `/healthz`、`/readyz` 均返回 `200 / 0.30.3`，数据库
  为 `ready`；
- 精确旧版领取载荷返回 `400`，错误字段为 `agentVersion`、`protocolVersion`；协议 `2`
  的空领取返回 `200` 和 `0` 条命令；
- 部署后 API warning/error 为 `0`；`hechao-package-publisher.service` PID 保持 `2064`，
  未重启；
- 2026-08-14 09:03 UTC 最终快照为 117 条，分布
  `default=100 / vip=14 / admin=0 / owner=3`；快照/身份差异、用户等级差异和活动命令均
  为 `0`；
- 本次上传暂存目录在正式链接和路径边界复核后删除；正式 release 与两份备份保留。

## 回滚

直接程序回滚目标为：

`/opt/hechao-launcher-api/releases/0.30.2-20260811T124943Z`

本版本没有数据库迁移。回滚只需原子恢复 `current` 并重启 API；正式 Tier Agent `0.1.3`
可继续兼容 `0.30.2`，但旧协议门禁会暂时失去服务端保护。回滚前必须确认等级命令队列
为空，不能删除、重放或手工改写真实玩家命令。

结构化证据见
[`evidence/API_0.30.3_PRODUCTION_DEPLOYMENT_2026-08-14.json`](evidence/API_0.30.3_PRODUCTION_DEPLOYMENT_2026-08-14.json)。

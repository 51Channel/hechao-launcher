# API 0.32.2 正式发布

- 正式发布 ID：`0.32.2-20260815T105349Z`
- 源码提交：`78e1d7ffc0a623e617faa3dbeea0e3a6b370c8c7`
- 正式标签：`api-v0.32.2`
- 生产切换时间：2026-08-15 18:54（Asia/Shanghai）
- 数据库迁移：无，生产保持 `30/30`

## 修复范围

生产目录中 `activity-survival` 已设为可见，但普通玩家的 `Production` 通道没有客户端
版本。旧 API 仍返回该服务器，导致服务器与客户端档案引用不完整；启动器拒绝整份目录并
回退缓存，连带阻断已有正式档案的“赫朝商务追杀”。

API 现在完成玩家通道和灰度解析后，只返回能够同时下发对应客户端档案的服务器。未发布
服务器继续保留在数据库和后台，不会被删除或误发布；具备正式档案的商务追杀不再受其
影响。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.32.2-20260815T105349Z.tar.gz` | 46,285,697 字节 | `3C6F3D569B00F6210AE287FA74DE3289C2F7456BF49C794A10ED3EC4C92FAC62` |
| `Hechao.Api` | 105,322,948 字节 | `82EBCA525A776340EC53DEF6E648F78BB175A507A9999F0809E7704D786DE44B` |

归档共 `161` 项、`156` 个文件，不含 PDB、环境文件或凭据。生产二进制与本机构建原件
大小和 SHA-256 一致。

## 测试与备份

- API `351/351`、完整 .NET 解决方案 `791/791`；
- `git diff --check` 通过；
- 部署前备份为
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.32.2-20260815T105349Z.tar.gz`，
  46,429,990 字节，SHA-256
  `B44EDB2BCB2255A2E49BB31DEE2C0A16FA9DF898728DB14FF6899486E7ECBDBB`；
- 备份包含旧 release、API 环境、systemd 与 Nginx 配置，并已通过归档读取校验。

## 生产验收

- API 原子切换到 `/opt/hechao-launcher-api/releases/0.32.2-20260815T105349Z`；
- 最终 PID `963896`、`NRestarts=0`，本机与公网健康、就绪和数据库均正常；
- 匿名实时目录由故障时的“2 台服务器、1 个档案”恢复为结构闭合的“1 台服务器、
  1 个档案”；唯一记录为 `activity / 赫朝商务追杀 / Online`；
- 公网 API、管理域名、官网和中转 API 均为 `200`；
- 发布后 API warning/error 为 `0`；Publisher PID `2064` 与 Nginx PID `1742715`
  保持不变；
- 没有启动、停止、重启 Minecraft，没有发送控制台命令，也没有改写服务器目录数据。

## 回滚

直接程序回滚目标为
`/opt/hechao-launcher-api/releases/0.32.1-20260815T071021Z`。本版没有数据库迁移，
回滚不需要数据库降级；如果新 API 未通过就绪检查，标准安装脚本会自动恢复旧链接。

结构化证据见
[`evidence/API_0.32.2_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/API_0.32.2_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

# API 0.32.1 正式发布

- 正式发布 ID：`0.32.1-20260815T071021Z`
- 源码提交：`71c6441e269cbf7d2e63d2129d45035b197dc1c5`
- 正式标签：`api-v0.32.1`
- 生产切换时间：2026-08-15 15:16（Asia/Shanghai）
- 数据库迁移：无，生产保持 `30/30`

## 发布范围

- `allowedCommandPrefixes=["*"]` 表示允许全部 Minecraft、模组和插件命令；
- API 心跳、控制台排队门禁和 Vue 后台使用同一通配合同；
- `stop/restart/shutdown/end` 始终拒绝自由控制台提交，继续使用结构化停止和重启；
- 后台显示“全部 Minecraft 与插件命令”，并保留快捷保存、玩家列表和白名单操作。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.32.1-20260815T071021Z.tar.gz` | 46,284,782 字节 | `BBAF89AB0776DC140078F9A3186CEA9D97F269BE6020E1847AA0A19F5BDE4971` |
| `Hechao.Api` | 105,322,436 字节 | `A0C848BD912B201411EDC938C8CC9A7ED85DFFC9C88F5975196592AF732D76D2` |

归档共 `161` 项、`156` 个文件，不含 PDB、生产环境文件或凭据。生产二进制与构建
原件哈希一致。

## 测试与备份

- API `350/350`、ServerControlAgent `76/76`、完整 .NET `790/790`；
- Vitest `13/13`、Playwright `33/33`、PowerShell 7 `47/47`；
- Vue 类型检查、生产构建和 `git diff --check` 通过；
- 部署前备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.32.1-20260815T071021Z.tar.gz`，
  46,431,017 字节，SHA-256
  `8D3AE12A8AD75AB2536D65F38CB23871C458DC2ED21B29E0A1AC70542DA987F5`。
  备份包含旧 release、API 环境、systemd 与 Nginx 配置，只保留在生产主机。

## 生产验收

- API 原子切换到
  `/opt/hechao-launcher-api/releases/0.32.1-20260815T071021Z`；最终 PID
  `848807`、`NRestarts=0`，健康、就绪和数据库均正常；
- Publisher PID `2064`、Nginx PID `1742715` 未变化，API 发布后 warning/error
  为 `0`；
- `launcher-api.hechao.world`、`admin.hechao.world`、`hechao.world` 与
  `api.hechao.world` 均返回 `200`；
- 生产后台静态资源包含全命令说明和生命周期按钮提示；
- owl5/owl9 最终共 `10/10` 目标以 Agent `0.7.2`、前缀 `*` 新鲜上报；
- 发布期间服控操作为 `0`，没有启动、停止、重启 Minecraft 或发送控制台命令。

## 回滚

API 直接程序回滚目标为
`/opt/hechao-launcher-api/releases/0.32.0-20260815T055857Z`。由于 `0.32.0` 不接受
`*` 心跳，回滚 API 前必须先按 Agent 发布记录恢复双机旧配置和兼容代理；不得只回滚
API 而让 `0.7.2` 继续上报通配前缀。

结构化证据见
[`evidence/API_0.32.1_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/API_0.32.1_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

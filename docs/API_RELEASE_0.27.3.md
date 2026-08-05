# API 0.27.3 正式发布

- 正式发布 ID：`0.27.3-20260805T184018Z`
- 制品源码提交：`a99fc915fdf271326cc8247078076ff4e0b5bf56`
- 正式标签：`api-v0.27.3`
- 生产切换时间：2026-08-06 02:42:46 CST

## 修复范围

整合包识别器现在会按内容识别任意命名的独立客户端与服务端顶层目录，并归一化为
安全的 `client/` 与 `server/` 布局。客户端目录内的 `.minecraft` 会正确去壳；
客户端 `libraries` 中嵌套的 `minecraft_server*.jar` 不再被误判为服务端根标记。

管理后台在分析含 Blocking issue 时明确显示“识别存在阻断”，不再固定显示
“识别无阻断”。本版没有数据库迁移、目录 API 或游戏服配置变化。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `0.27.3-20260805T184018Z.tar.gz` | 45,941,391 字节 | `86C994DE0B1BEE9640419D1A78AC1E98BC78089505DE4839198FF3F769080AA0` |
| `Hechao.Api` | 105,026,500 字节 | `7D0ED116BA90992A2493104F56DC697E1A0046EE9AA88CA70D7DD6DB7568C44B` |

生产当前链接为
`/opt/hechao-launcher-api/releases/0.27.3-20260805T184018Z`，远端归档与二进制哈希
均和本机构建制品一致。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260805T184226Z.dump`；
- API 与配置备份：`/var/backups/hechao-launcher/releases/20260805T184220Z`；
- 误识别分析快照：上述目录内的
  `task-777a31bfacc947549f4ba3a2e5be95f1-analysis-0.27.2.json`；
- 原子安装脚本切换 `current`，就绪失败会自动恢复
  `0.27.2-20260805T180006Z`；
- 只重启 `hechao-launcher-api.service`，没有操作 Publisher、Nginx、Velocity 或任何
  Minecraft 服务端。

## 验证

- Modpack `6/6`、API `275/275`、Vitest `8/8`、Playwright `14/14`；
- 四个相关 .NET 项目格式检查通过，Impeccable detector 为零问题；
- 本机与公网 `/healthz`、`/readyz` 均返回 `0.27.3`，数据库为 `ready`；
- API 为 `active/running`、PID `657987`、`NRestarts=0`，发布后错误级 journal 为 `0`；
- Nginx PID 保持 `459682`、`NRestarts=0`；Publisher 保持发布前的
  `inactive/dead`；
- 真实任务 `777a31bf-acc9-4754-9f4b-a3a2e5be95f1` 复用原始 ZIP 重新识别为
  `Canonical`：客户端 4,355 文件、服务端 2,303 文件、Blocking issue 为 0；
- 客户端归档 SHA-256 为
  `F38A1EF5B05B6793184F98475FE90BCB0E88A137298D33BE004020102FA8DBC7`，服务端归档
  SHA-256 为 `5C9DE42780618671E5DD48A4C92E82EEB0335AA8B68C61D9EDE5AD6B64C0B5AB`；
- 任务最终保持 `AwaitingReview r7`，没有进入客户端发布或服务端部署。

## 回滚

本版没有 Schema 变化，直接回滚目标为
`/opt/hechao-launcher-api/releases/0.27.2-20260805T180006Z`。回滚只切换 API 的
`current` 链接并复核健康。任务的新分析结果与归档使用新规则生成；若回滚 API，必须先
取消该任务或保持等待确认，不得让旧识别器继续发布。

结构化证据见
[`evidence/API_0.27.3_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/API_0.27.3_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

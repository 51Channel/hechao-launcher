# API 0.27.1 正式发布

- 正式发布 ID：`0.27.1-20260804T211905Z`
- 制品源码提交：`b1f049563774c8a941afbe283bc072c011f4fb8e`
- 正式标签：`api-v0.27.1`
- 生产切换时间：2026-08-05 05:23:05 CST
- 最终生产复核：2026-08-05 05:52 CST

## 发布范围

`0.27.1` 在 `0.27.0` 的统一账号、目录、分发、整合包导入和服控能力上增加两个面向
官网的匿名只读边界：

- `GET /v1/public/activities` 只投影可见活动的名称、公告、排期、解析后状态、人数、
  Minecraft 版本、加载器和最低等级，不返回客户端档案、下载地址、服控目标或审计数据；
- `GET /v1/public/launcher/latest` 返回当前正式安装包的版本、大小、SHA-256、发布日期和
  更新说明，不返回 OSS 地址；
- `GET /v1/public/launcher/download` 即时签发短期 HTTPS 地址并返回 `302`，安装包正文
  继续由私有 OSS 直接下发；
- 三个入口使用独立匿名限流，官网只经同机 `127.0.0.1:8090` 调用。日志、文档和验收
  证据均不保存完整签名地址。

本版没有新增数据库迁移，生产迁移仍为 `23/23`。原有鉴权
`GET /v1/launcher/update` 继续拒绝匿名请求；管理后台 Host 锁定、整合包导入、
Publisher 和 ServerControlAgent 均未改变。本次只部署 API，没有发布或覆盖启动器
`0.14.2` 的 OSS 安装包。

## 正式制品

| 制品 | SHA-256 |
| --- | --- |
| `hechao-api-0.27.1-20260804T211905Z.tar.gz` | `706969132DAD49D5B69CE91858FBC423CE2A4F85F60DFA62C0DCF37D3477AABC` |
| `Hechao.Api` | `F68790888A1DBFF6AC8C973F530E28697B6A901A98458179C4D7D37C8DE2D796` |

生产当前链接为
`/opt/hechao-launcher-api/releases/0.27.1-20260804T211905Z`。远端二进制为
`105,020,356` 字节，SHA-256 与构建制品一致。

## 备份与部署

- 切换前 API、环境引用、systemd、Nginx、数据保护和清单快照位于
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.27.1-20260804T211905Z`；
  `12` 个文件全部通过 `SHA256SUMS`，清单自身 SHA-256 为
  `28D599F5180F1DADA4952A272D35791E2B3F3481BF0CFC639A345E99FD7EF81`。
- 上传归档和远端解包内容完成哈希核对后，由既有原子安装脚本切换 `current`；就绪失败
  路径继续自动恢复 `0.27.0-20260803T174833Z`。
- 本版没有执行数据库迁移、目录写入、Publisher 任务或 Minecraft 服控命令，也没有
  上传、覆盖或下载 OSS 安装包正文。

## 验证

- API：`273/273`；
- 启动器：`219/219`；
- 完整解决方案：`655/655`；
- 本次修改文件格式检查、PowerShell 7 合规和发布台账检查通过；
- `/healthz` 与 `/readyz` 均返回 `0.27.1`，数据库为 `ready`；
- `hechao-launcher-api.service` 为 `active/running`，PID `4041901`、
  `NRestarts=0`，只监听 `127.0.0.1:8090`；发布后错误级 journal 为 `0`；
- 公开活动投影当前为 `0` 条；公开启动器元数据返回正式版 `0.14.2`；下载入口返回短期
  HTTPS `302`；匿名旧更新入口为 `401`，错误 Host 的 `/admin/` 为 `404`；
- 官网 `https://hechao.world/download` 已在后续独立 release 中接入并返回 `200`；公网
  `8090` 不可连接。

## 回滚

本版没有 Schema 变化，直接 API 回滚目标为
`/opt/hechao-launcher-api/releases/0.27.0-20260803T174833Z`。回滚只切换 API 的
`current` 链接并复核本机及公网健康，不需要回滚网站 SQLite，也不得修改 Minecraft
服务端。旧 API 会让官网活动投影和下载页进入降级状态，但不会使社区、报名活动、企划
日程或已有上传不可用。

结构化证据见
[`evidence/API_0.27.1_PRODUCTION_DEPLOYMENT_2026-08-05.json`](evidence/API_0.27.1_PRODUCTION_DEPLOYMENT_2026-08-05.json)。

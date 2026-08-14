# API 0.30.4 正式发布

- 正式发布 ID：`0.30.4-20260814T093000Z`
- 制品源码提交：`f0dd3b3c06e1bc0d2c173a5d0e1ba34b95698163`
- 正式标签：`api-v0.30.4`
- 生产切换时间：2026-08-14 17:32（Asia/Shanghai）
- 数据库迁移：无，保持 `028`

## 发布范围

后台显示“等待 Publisher 工作空间”的根因是 API 主机日志占用过高，而不是上传任务卡死
或客户端归档损坏。ASP.NET Core 内部心跳与轮询产生的 Information 请求日志同时进入
journald 和 syslog，最终触发 Publisher 的磁盘安全门禁。

本版把 `Microsoft.AspNetCore` 日志最低级别设为 `Warning`，并部署 journald 策略：持久
journal 上限 `1 GiB`、文件系统至少保留 `8 GiB`、最长保留 `14` 天。现有日志经受控
轮转和压缩后，根分区可用空间由约 `9.3 GiB` 提升到约 `13 GiB`。安全门禁本身没有
降低，Publisher 的展开倍率、保留空间和 OSS 发布逻辑均未改变。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.4-20260814T093000Z.tar.gz` | 46,247,238 字节 | `B841A57E988F5F64318FBBD531F370E785680022B09D45D2B0F0ADCC2754CEC5` |
| `Hechao.Api` | 105,245,636 字节 | `F8E5E020AA0F81CEE7F8F86A5A9D066C38DAD2C044D10B20FBADA7C0F70D160A` |

归档共 `161` 项、`156` 个文件；本地与生产二进制大小和 SHA-256 一致。

## 测试与备份

- Vitest `11/11`、Playwright `26/26`、API `.NET 313/313`、完整解决方案
  `.NET 733/733`；
- 数据库 custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260814T093110Z.dump`，
  6,205,892 字节，SHA-256
  `8157BEBFD2790D406E6F8E6749A07D14D1A62C6A60490691C756E34642512281`；旁车校验与
  `pg_restore --list` 均通过，目录记录 `239` 行；
- API 环境、systemd、Nginx 和完整 `0.30.3` release 快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.4-20260814T093112Z.tar.gz`，
  46,133,251 字节，SHA-256
  `FE3E5552B3275D259071C30504280638375A7387F1E87DC90A77EAD4FEC3BB3E`；
- journald 原配置备份：
  `/var/backups/hechao-launcher/journald-predeploy/20260814T092801Z`。

## 生产验收

- API 原子切换到 `/opt/hechao-launcher-api/releases/0.30.4-20260814T093000Z`，
  `/healthz` 与 `/readyz` 的本机和公网响应均为 `200`、版本 `0.30.4`、数据库 `ready`；
- API PID `132120`、`NRestarts=0`，只监听 `127.0.0.1:8090`；Publisher PID `2064`、
  Nginx PID `1742715` 均未变化；
- 50 次连续健康请求的 syslog 增量为 `0`，对应 API journal 无新业务日志、warning/error
  为 `0`；journal 占用约 `991 MiB`，配置上限与保留空间均已生效；
- 受影响的整合包任务已自动完成，数据库最终状态为 `Completed`；
- 本次没有数据库迁移、OSS 覆盖、Publisher 重启、Nginx 重启或 Minecraft 服控命令。

## 回滚

程序回滚目标为
`/opt/hechao-launcher-api/releases/0.30.3-20260814T072942Z`。原子安装脚本在就绪失败时
会自动恢复该链接。journald 策略可从上述独立备份恢复；回滚日志策略前必须确认仍有足够
Publisher 工作空间，不能通过降低安全门禁换取任务继续。

结构化证据见
[`evidence/API_0.30.4_PRODUCTION_DEPLOYMENT_2026-08-14.json`](evidence/API_0.30.4_PRODUCTION_DEPLOYMENT_2026-08-14.json)。

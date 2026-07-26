# 启动器 API 0.13.0 发布记录

> 发布 ID：`0.13.0-20260726T173536Z`
>
> 状态：生产在线
>
> 直接回滚目标：`0.12.0-20260725T203001Z`

## 变更

API 新增玩家主动诊断上传闭环：

- 玩家必须先在本机生成脱敏 ZIP，再单独确认上传。
- 上传授权有效期 10 分钟且只能使用一次，令牌不进入 URL、日志或审计。
- 服务端复验大小、SHA-256、固定 ZIP 条目、解压后大小和档案 ID。
- 每账号限制每日次数、每日字节数和同时保留数量。
- 诊断文件位于 Web 根目录之外，默认保留 14 天并自动销毁。
- 管理员列表与下载要求 MFA；每次下载写入审计。

## 发布物

| 项目 | 值 |
| --- | --- |
| 单文件大小 | `103,796,275` 字节 |
| 单文件 SHA-256 | `F2B7466A9AFAB142F110D7C2EB692DE1BA2FDD653F7CF42D4AE31D5BF7E8C811` |
| 归档 | `artifacts/releases/hechao-api-0.13.0-20260726T173536Z.tar.gz` |
| 归档大小 | `45,339,427` 字节 |
| 归档 SHA-256 | `E7C8DECAFD8A3B47EB63987F8542C8BB034AB86C831F32B242F741FE26ABC728` |

## 备份

- 完整 API、配置、systemd 与 Nginx 备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.13.0-full-20260726T173217Z.tar.gz`
- 完整备份大小：`45,525,654` 字节
- 完整备份 SHA-256：
  `8868A57C5482C47406AC83F2B847FF8389ECB7B64FFF5B00B86C33D66846C23D`
- 数据库：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260726T173201Z.dump`
- 数据库大小：`88,525` 字节
- 数据库 SHA-256：
  `DAEB5CE12E3ED23561A734FB8A228598DCA20F117A450E4CBB2D4029016EB14C`
- 诊断配置写入前环境备份：
  `/var/backups/hechao-launcher/api-configuration/environment-before-diagnostic-uploads-20260726T173643Z`

较早的
`/var/backups/hechao-launcher/api-predeploy/pre-api-0.13.0-20260726T173201Z.tar.gz`
只归档了 `current` 符号链接，不能作为完整回滚备份。

## 生产验收

- 本机与公网 `/healthz`、`/readyz` 返回 200，数据库状态为 `ready`。
- 迁移 `9`、`launcher.diagnostic_uploads` 和三个诊断索引存在。
- 存储目录为 `hechao-api:hechao-api 0700`，systemd 只给 API 写入诊断目录和
  Data Protection 目录。
- 合成链路结果：
  - 创建授权 `201`
  - 错误令牌 `404`
  - 合法上传 `200`
  - 重复上传 `404`
  - 缺少令牌 `400`
  - 错误哈希 `400`
- 数据库状态分别为 `uploaded` 与 `failed`，授权、完成和失败审计数量一致。
- 将成功夹具到期时间置为过去并重启 API 后，记录变为 `expired`、写入过期审计，
  物理 ZIP 被删除。
- 合成账号、会话、上传和临时文件已精确清理，生产合成账号与上传残留均为 `0`。
- `hechao.world`、`api.hechao.world`、`admin.hechao.world` 均返回 200；
  `launcher-api.hechao.world/admin/` 保持 404。
- 完整解决方案测试为 `237/237`，Velocity 测试为 `11/11`。
- 最近 systemd journal 无 warning 或 error。

## 尚未宣称完成的验收

生产管理员 MFA 凭据数仍为 `0`，因此管理员下载真实 ZIP 及
`diagnostic.admin.downloaded` 生产审计尚未执行。该项必须在登记真实管理员 TOTP
后完成，不能用绕过 MFA 的数据库夹具替代。

# 诊断包上传、审计与销毁

> API：`0.13.0`
>
> 启动器：`0.11.16`
>
> 默认保留：`14` 天

## 1. 隐私边界

诊断上传永远由玩家分两步发起：

1. 玩家先在本机生成脱敏 ZIP。
2. 玩家再次点击“上传给管理员”，阅读保存期限与审计说明并确认。

启动器不会自动上传日志。生成操作仍会打开本地诊断目录，取消上传不会删除本地
文件。上传内容继续遵守 [`GAME_DIAGNOSTICS.md`](GAME_DIAGNOSTICS.md) 的固定条目、
路径脱敏、单文件尾部截取和世界存档排除规则。

## 2. 接口

- `POST /v1/diagnostics/uploads`
  - 要求有效赫朝启动器会话。
  - 提交档案 ID、大小、SHA-256 和启动器版本。
  - 返回 10 分钟有效、仅可使用一次的上传令牌。
- `PUT /v1/diagnostics/uploads/{id}`
  - 令牌只通过 `X-Hechao-Diagnostic-Token` 请求头传递。
  - 不把令牌放在 URL、数据库明文或审计数据中。
  - 只接受 `application/zip` 或 `application/octet-stream`。
- `GET /v1/admin/diagnostics`
  - 只允许完成 MFA 的管理员 Web 会话。
  - 只返回尚未到期的元数据。
- `GET /v1/admin/diagnostics/{id}/download`
  - 下载原始脱敏 ZIP。
  - 每次下载写入 `diagnostic.admin.downloaded` 审计事件。

## 3. 服务端限制

- 单个 ZIP 最大 `8 MiB`。
- 每账号 24 小时最多创建 `5` 次上传授权。
- 每账号 24 小时授权总大小最多 `40 MiB`。
- 每账号最多保留 `10` 个尚未到期的诊断包。
- ZIP 最多 4 个固定条目，解压后总大小最多 `2 MiB`。
- 必须包含 `diagnostic.json` 与 `README.txt`。
- 元数据 schema 必须为 `1`，档案 ID 必须与授权一致。
- 拒绝路径穿越、世界存档、任意日志、重复条目、哈希不符和大小不符。

失败或取消的上传不能复用原令牌。玩家可以保留本地 ZIP，重新确认并申请新令牌。

## 4. 存储与销毁

生产存储目录：

```text
/var/lib/hechao-launcher-api/diagnostics
```

- 目录所有者为 `hechao-api:hechao-api`，权限 `0700`。
- 文件位于 Web 根目录之外。
- systemd 仅给 API 进程该目录的写权限。
- 上传先写入随机 ID 对应的 `.part`，校验全部通过后原子改名为 `.zip`。
- 后台任务每小时将到期记录标记为 `expired`，写审计并删除 `.part` 与 `.zip`。
- 过期两轮清理间隔以上的孤立 `.part` 也会删除。

配置脚本：

```bash
sudo ./configure-diagnostic-uploads.sh
```

脚本会备份 `/etc/hechao-launcher-api/environment`、创建存储目录并写入全部
`DiagnosticUploads__*` 参数，但不会自行重启 API。

## 5. 审计动作

| 动作 | 含义 |
| --- | --- |
| `diagnostic.upload.authorized` | 玩家确认后取得一次性上传授权 |
| `diagnostic.upload.completed` | ZIP 大小、哈希和结构全部通过 |
| `diagnostic.upload.failed` | 本次令牌已消费但上传或校验失败 |
| `diagnostic.admin.downloaded` | 完成 MFA 的管理员下载 |
| `diagnostic.upload.expired` | 到期记录已失效并进入文件清理 |

审计不会保存上传令牌、日志文本或诊断 ZIP 内容。

## 6. 部署验收

1. 部署前执行数据库自定义格式备份，并通过 `sha256sum -c` 和
   `pg_restore --list`。
2. 运行 `configure-diagnostic-uploads.sh`，确认环境文件仍为 `root:root 600`。
3. 部署 API 后确认迁移 `9`、存储目录 `0700` 和服务沙箱写权限。
4. 以临时会话创建授权，验证无令牌、错误令牌、错误哈希和重复 PUT 均失败。
5. 上传固定脱敏夹具，确认管理列表可见、下载 SHA-256 一致且审计新增。
6. 将夹具到期时间置为过去，执行一次清理，确认文件、列表和数据库状态一致。
7. 精确删除临时账号、会话和夹具审计；不得清理真实玩家数据。

## 7. 生产证据

`0.13.0-20260726T173536Z` 已于 2026-07-27 完成迁移 9、存储权限、systemd
沙箱、公网健康与旧业务回归。合成验收依次得到创建 `201`、错误令牌 `404`、上传
`200`、重复上传 `404`、缺令牌 `400`、错误哈希 `400`。成功夹具强制到期后，
后台任务将记录改为 `expired`、写入 `diagnostic.upload.expired` 并删除物理 ZIP。
合成账号、会话、上传和文件已精确清理。

2026-07-27 已由真实 `0.11.14` 上传编号 `1e707520`，管理员完成 MFA 后在生产后台
实际下载。上传端、生产端和下载文件均为 `707` 字节，SHA-256 均为
`1C53C309DDA3D1D9A905836E79A041EDCD4DDD03C543E0424119C876AAA6BF92`；
`diagnostic.upload.authorized`、`diagnostic.upload.completed` 和
`diagnostic.admin.downloaded` 审计全部存在。详细制品、备份和哈希见
[`API_RELEASE_0.13.0.md`](API_RELEASE_0.13.0.md) 与
[`evidence/ADMIN_MFA_DIAGNOSTIC_UPLOAD_2026-07-27.json`](evidence/ADMIN_MFA_DIAGNOSTIC_UPLOAD_2026-07-27.json)。

# API 0.30.7 正式发布

- 正式发布 ID：`0.30.7-20260814T144949Z`
- 制品源码提交：`c16c0a1310ab4a2cade075520ce4ec7f624e9eb8`
- 功能源码提交：`a5696c5ee9b2b8934c024579f9c411d5351a6a2f`
- 正式标签：`api-v0.30.7`
- 生产切换时间：2026-08-14 23:04（Asia/Shanghai）
- 数据库迁移：无，保持 `028`

## 发布范围

本版将活动客户端的提前下载权限与最终进服权限正式分离：

- 已登录玩家始终能看到可见的玩家活动、排期和客户端档案；
- 目录新增向后兼容的 `canJoin` 字段，最低称号不足时保留下载入口并禁用进服；
- 单服 `Allow` 可越过最低称号，单服 `Deny` 优先于称号等级；
- 永久服继续隐藏无权目录记录，不扩大原有可见范围；
- 隐藏活动、基础设施、封禁身份和无可用发布仍不能取得客户端；
- Velocity 一次性授权与服务端最终门禁不变。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.7-20260814T144949Z.tar.gz` | 46,260,374 字节 | `21894901792A8791BA89F704ED74A1144360CBD70974F573673246F5591D36AC` |
| `Hechao.Api` | 105,257,924 字节 | `6523572993E4848B2BD1CA0E743FBE10E4A07FE0F617BDCD3B514C39225F2190` |

归档共 `161` 项、`156` 个文件；路径安全检查通过，生产二进制大小和 SHA-256 与正式
制品一致。

## 测试与备份

- API `.NET` `326/326`、Launcher `.NET` `229/229`、完整解决方案 `.NET`
  `748/748`；C# 文件格式、XAML XML、发布文件和 `git diff --check` 通过；
- 数据库 custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260814T150128Z.dump`，
  6,283,603 字节，SHA-256
  `EF7F2459CD619105CD4A02B6B9A2F24E60F54CE1DFE3D20CCFACAB01075D4190`；
  `pg_restore --list` 成功读取 `239` 行；
- API 环境、systemd、Nginx 和完整 `0.30.6` release 快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.7-20260814T150310Z.tar.gz`，
  50,790,914 字节，SHA-256
  `D95743031D038ABD97B2C01A638E57C447FBC928DC5070A58576E5A84B5CC349`。

## 生产验收

- API 原子切换到 `/opt/hechao-launcher-api/releases/0.30.7-20260814T144949Z`；本机和
  公网 `/healthz`、`/readyz` 均为 `200`、版本 `0.30.7`、数据库 `ready`；
- API PID `318675`、`NRestarts=0`，只监听 `127.0.0.1:8090`；Publisher PID `2064`
  与 Nginx PID `1742715` 未变化，公网 `8090` 保持关闭；
- 数据库迁移为 `28/28`，发布中整合包任务和待执行服控任务均为 `0`，发布后
  warning/error 为 `0`；`hechao.world`、`api.hechao.world` 和
  `admin.hechao.world` 均返回 `200`；
- 生产真实认证探针确认：`Member` 可见 `activity / 赫朝商务追杀`、取得绑定客户端档案
  和签名清单 `200`，但 `canJoin=false`；有效单服 `Allow` 后为 `true`；账号满足
  `Participant` 时加入单服 `Deny` 后仍为 `false`，签名清单仍返回 `200`；
- 验收发现活动最低称号曾被临时改为 `Member`。按本版方案原子恢复为 `Participant`
  并升至修订 `17`，排期、客户端档案和 Velocity 目标保持不变。变更前定向快照为
  `/var/backups/hechao-launcher/catalog/activity-access-before-plan2-20260814T152615Z.json`，
  SHA-256 为
  `929805B785A03C43E7E654703C3A9220C0E6AE6D18A82427424E675762E74B69`；
- 临时验收账号、会话、Minecraft 身份和单服例外已精确删除，剩余记录为 `0`；
- 活动验收时尚未进入开放窗口，真实 launch-grant 按预期返回 `403`，无法据此区分
  `ServerUnavailable` 与称号拒绝。Velocity 最终称号、`Allow` 和 `Deny` 顺序由自动化
  测试覆盖，真人开放窗口验收仍保留；
- 本次没有数据库迁移、OSS 覆盖、Publisher/Nginx 重启或 Minecraft、Velocity、服控
  操作。远端临时上传目录已删除。

## 回滚

程序直接回滚目标为
`/opt/hechao-launcher-api/releases/0.30.6-20260814T133415Z`。原子安装脚本在就绪检查
失败时会自动恢复该链接；迁移保持 `028`，不执行数据库降级。程序回滚不会自动把活动
最低称号改回 `Member`；如业务明确撤销方案 2，应通过后台产生新修订，不能覆盖修订
`17` 或直接还原旧数据库。

结构化证据见
[`evidence/API_0.30.7_PRODUCTION_DEPLOYMENT_2026-08-14.json`](evidence/API_0.30.7_PRODUCTION_DEPLOYMENT_2026-08-14.json)。

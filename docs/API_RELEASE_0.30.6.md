# API 0.30.6 正式发布

- 正式发布 ID：`0.30.6-20260814T133415Z`
- 制品源码提交：`171cea3e4cd2404e56d0ac90b8ef6d64facb8322`
- 正式标签：`api-v0.30.6`
- 生产切换时间：2026-08-14 21:58（Asia/Shanghai）
- 数据库迁移：无，保持 `028`

## 发布范围

本版修复启动器已有活动排期、但后台活动企划月历完全空白的问题：

- 活动企划接口新增 `unmanagedSchedules`，只查询 `velocity_target=activity`、已有开放或
  结束时间、但 `activity_plan_status IS NULL` 的旧服务器目录排期；
- 后台将旧排期显示为独立告警和蓝色虚线日历事件，点击后列出时间、客户端档案、
  整合包绑定和缺失项；
- 旧排期保持只读，不计入正式企划统计，不能拖动、发布、部署或触发 Minecraft 服控；
- `赫朝商务追杀` 当前只有北京时间 2026-08-15 17:00 的开放时间，缺少结束时间、
  正式企划状态和整合包企划绑定。本版如实显示，不猜测结束时间或强制转换。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.6-20260814T133415Z.tar.gz` | 46,259,308 字节 | `17B6995E8349A6024BDEA26AD84B86F578FC3EBB918ED7B3D4DA6F0F914D5025` |
| `Hechao.Api` | 105,253,828 字节 | `6CF985C4EEC6299A393C344A26BE031D6CB641C4790B40E54EE9A2EE870D353E` |

归档共 `161` 项、`156` 个文件；生产二进制大小和 SHA-256 与正式制品一致。

## 测试与备份

- TypeScript 类型检查、Vitest `13/13`、Playwright `31/31`、API `.NET 317/317`、
  完整解决方案 `.NET 737/737`、发布溯源 `20/20`；
- Playwright 覆盖旧排期可见、缺失项、无发布/部署操作及 `390px` 移动端无横向溢出；
- 数据库 custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260814T135715Z.dump`，
  6,256,835 字节，SHA-256
  `A2789EA70B9BBA4E23E6ABE8080506AA120C9A07CBD6AF7EEA9DFB56216F4B4B`；
  `pg_restore --list` 成功读取 `239` 行；
- API 环境、systemd、Nginx 和完整 `0.30.5` release 快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.6-20260814T135715Z.tar.gz`，
  46,390,319 字节，SHA-256
  `28BA56C614B4739C04FFE936843144A63DB623ABA73DF543E048C7FFE91D8192`。

## 生产验收

- API 原子切换到 `/opt/hechao-launcher-api/releases/0.30.6-20260814T133415Z`；本机与
  公网 `/healthz`、`/readyz` 均为 `200`、版本 `0.30.6`、数据库 `ready`；
- API PID `282428`、`NRestarts=0`，只监听 `127.0.0.1:8090`；Publisher PID `2064`
  与 Nginx PID `1742715` 均未变化，公网 `8090` 保持关闭；
- 数据库迁移为 `28/28`，正式企划、发布中整合包任务和待执行服控任务均为 `0`，
  发布后 warning/error 为 `0`；
- 生产查询精确命中一条旧排期：`activity / 赫朝商务追杀 / 2026-08-15 09:00 UTC`，
  `closes_at` 与 `activity_package_import_id` 均为空，客户端档案为
  `hechao-business-manhunt-paper-1.21.11`；
- 生产后台静态资源包含 `unmanagedSchedules` 与“目录排期未纳入企划”，未认证访问
  `/v1/admin/activity-plans` 仍返回 `401`；
- `hechao.world`、`api.hechao.world`、`admin.hechao.world` 均返回 `200`；
- 本次没有数据库迁移、OSS 覆盖、Publisher/Nginx 重启或 Minecraft 服控命令。

## 回滚

程序直接回滚目标为
`/opt/hechao-launcher-api/releases/0.30.5-20260814T121420Z`。原子安装脚本在就绪检查
失败时会自动恢复该链接；迁移保持 `028`，不执行数据库降级。回滚不会撤销服务器目录、
整合包、服控或审计数据。

结构化证据见
[`evidence/API_0.30.6_PRODUCTION_DEPLOYMENT_2026-08-14.json`](evidence/API_0.30.6_PRODUCTION_DEPLOYMENT_2026-08-14.json)。

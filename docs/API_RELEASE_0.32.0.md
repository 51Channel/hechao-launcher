# API 0.32.0 正式发布

- 正式发布 ID：`0.32.0-20260815T055857Z`
- 源码提交：`e7fd989a5edacfafba3999ecf8b7ccef183b7e19`
- 正式标签：`api-v0.32.0`
- 生产切换时间：2026-08-15 14:14（Asia/Shanghai）
- 数据库迁移：新增 `030_independent_deployment_slots.sql`，生产为 `30/30`

## 发布范围

- 动态部署槽按 `Activity`、`Survival`、`Pvp`、`Minigame` 四种用途创建，对应
  `activity-*`、`survival-*`、`pvp-*`、`minigame-*` ID；
- 独立槽从 `25600-25611` 分配端口，Velocity 目标等于槽 ID，冲突组为空，因此不同
  类型的槽可以同时运行；
- 固定 `activity / 25568 / owl5-activity-slot` 继续只服务旧替换入口，同一入口内的
  替换服仍互斥；
- API 只为 `Ready` 独立槽向 Authorizer 返回受控回环地址和批准端口；创建和部署均不
  自动启动 Minecraft。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.32.0-20260815T055857Z.tar.gz` | 46,284,421 字节 | `2FB737D67F97887BA1E747CC93CB2E654B02791D3B673F041C55CEB087A43B43` |
| `Hechao.Api` | 105,321,924 字节 | `687CBCC777330ABCEC6293F5A5CAE3CED178ED019DE4512AAEEF12C29908E4D3` |

归档共 `161` 项，不含 PDB、环境文件或凭据。Windows 构建的 tar 不保留 Unix 模式，
部署时按既有正式基线归一化为目录 `755`、普通文件 `444`、主程序 `555`；生产二进制
与构建原件哈希一致。

## 测试与备份

- API `345/345`、ServerControlAgent `71/71`、完整 .NET `780/780`；
- Velocity Authorizer `31/31`、Vitest `13/13`、Playwright `32/32`、PowerShell 7
  脚本 `47/47`、发布溯源 `20/20`，Vue 类型检查、生产构建与 `git diff --check` 通过；
- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260815T060627Z.dump`，
  6,426,936 字节，SHA-256
  `EAF89B23C5078F93DD7C29F5D629F625ECEAA5A49788C1F481A81BE4C730C5CF`，
  `pg_restore --list` 读取 `248` 行；
- API、环境、systemd 与 Nginx 配置备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.32.0-20260815T055857Z.tar.gz`，
  46,411,335 字节，SHA-256
  `A8F071EEA4CAB5F6B9CE163B0F43A1DF09B7B620258094E124FCCD1928F3003B`。

## 生产验收

- API 原子切换到 `/opt/hechao-launcher-api/releases/0.32.0-20260815T055857Z`；最终
  PID `812015`、`NRestarts=0`，内外网健康与就绪均为 `200`、数据库为 `ready`；
- 迁移 `30/30`，工业季槽迁移为
  `Survival / 25600 / activity-survival / Ready`，目录继续为 `Closed / hidden`；
- owl5 八个目标均以代理 `0.7.0` 新鲜上报，活动、整合包和服控进行中任务均为 `0`；
- API 仍只监听 `127.0.0.1:8090`；Publisher PID `2064`、Nginx PID `1742715` 未变，
  最终切换后 warning/error 为 `0`；
- `launcher-api.hechao.world`、`admin.hechao.world`、`hechao.world` 与
  `api.hechao.world` 均返回 `200`；未登录创建槽 `POST` 返回 `401`；
- 本次发布没有发送 Minecraft 启停、重启或控制台命令。

发布门禁执行过两次安全回退。第一次在切换前发现 tar 缺少 Linux 可执行位，线上进程和
数据库未变化；第二次已启动 `0.32.0` 并应用迁移 030，但验收脚本误用 `GET` 检查只接受
`POST` 的端点，把正确的 `405` 误判为失败。第二次按预案恢复 `0.31.0`，加法迁移安全
保留；修正为未登录 `POST -> 401` 后完成最终切换。两次均不是 API 或数据库故障。

## 回滚

程序直接回滚目标为
`/opt/hechao-launcher-api/releases/0.31.0-20260815T024200Z`。迁移 030 为加法迁移，
程序回滚时保留用途、端口和 Velocity 目标列。若连同独立槽能力一起回退，必须先停止
owl5 服控代理，按 ServerControlAgent `0.7.0` 发布记录恢复工业季的数据库、状态文件和
`server.properties`，且不得启动工业季或其他 Minecraft 服务端。

结构化证据见
[`evidence/API_0.32.0_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/API_0.32.0_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

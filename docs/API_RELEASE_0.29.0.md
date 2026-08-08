# API 0.29.0 正式发布

- 正式发布 ID：`0.29.0-20260808T043921Z`
- 制品源码提交：`43da4ae05db64e50475a19e49f3e475410394dbd`
- 功能提交：`21950f6b7b3b66abe2e9c98d15427d1df3600852`
- 启动修复提交：`43da4ae05db64e50475a19e49f3e475410394dbd`
- 正式标签：`api-v0.29.0`
- 生产切换时间：2026-08-08 12:43 CST
- 数据库迁移：`027_client_profile_lifecycle`

## 功能范围

- 客户端档案支持归档、恢复和受限永久删除，操作要求管理员 MFA、CSRF、当前修订号、
  原因和审计；
- 归档要求零服务器引用并保留不可变版本、发布通道、签名清单和 OSS 对象；恢复后固定
  为停用状态，不会自动重新分发；
- 永久删除只允许已归档、零版本、零服务器引用的空草稿，并要求精确输入
  `DELETE <profileId>`；
- API、Publisher 和整合包最终化事务都会拒绝向已归档档案继续写入发布状态；
- Vue 后台增加“使用中 / 已归档 / 全部”筛选、生命周期抽屉、限制原因和审计标签。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.29.0-20260808T043921Z.tar.gz` | 45,974,811 字节 | `79D08B0D5802518E0450A6594D911D3D5A08DD4296F4F583D5FA8F455E92E41D` |
| `Hechao.Api` | 105,084,356 字节 | `2A9DFCFE97592D7E4B2930D5E2E39754D5262500185319B32BC8C03047A3ACE0` |
| `wwwroot/admin/assets/admin.css` | 61,020 字节 | `218D1C461CB32BCA3E2D15E3302D3732F5570FF347CC1CD6E202395296E116CD` |
| `wwwroot/admin/assets/chunk-ProfilesView.js` | 25,563 字节 | `114E47CB95D58D3C0C6B816C8CA1B7495E0B3778AD739007D174CA991F343DB2` |

归档共 157 项、153 个文件，只含单文件 API、静态资源端点清单和 `wwwroot`；不含
PDB、环境文件、生产配置或凭据。服务器重新计算的归档、安装器、service 和迁移 SQL
哈希均与本地一致，归档没有绝对路径、`..` 或敏感文件条目。二进制内含产品版本
`0.29.0` 和源码提交 `43da4ae05db64e50475a19e49f3e475410394dbd`。

## 备份与迁移演练

- 迁移前 PostgreSQL custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260808T042718Z.dump`，
  4,224,057 字节，SHA-256
  `4CBE6DCD58F33DA98C6160B916A6A796406B5A82BE4B0AA2C881C4F684264A98`；
- 迁移后的重试恢复点：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260808T044309Z.dump`，
  4,229,303 字节，SHA-256
  `4FCEFE87F9A5AB92774BA8FC847DD0C59D8F16296D2C75B15E011CF4EFD0A20D`；
- 两份校验文件均通过；`pg_restore --list` 分别读取 225 和 227 行；
- 环境文件、systemd、Nginx 站点、旧 `current` 链接和旧二进制哈希备份：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.29.0-20260808T042752Z.tar.gz`，
  3,181 字节，SHA-256
  `3AC9AB1A45965F74061B4847D2A4D5017B51C79C22ED0FC14D9EB40C72C9CE88`；
- 迁移前备份已恢复到唯一临时数据库并准确应用 `027`。三列、外键、检查约束和索引
  均存在且有效，检查约束真实拒绝非法状态；8 个档案和 10 个版本保持不变，临时数据库
  随后删除。

## 部署过程

首次制品 `0.29.0-20260808T042002Z` 在 2026-08-08 12:33 CST 启动时触发
ASP.NET 10 路由绑定错误：`DELETE` 端点不能自动推断请求体。安装器就绪门禁自动恢复
`0.28.7`，公网健康恢复正常，Publisher 与 Nginx 未变化。迁移 `027` 已在该次启动中
事务提交，但所有 8 个档案仍为未归档默认状态。

随后提交 `43da4ae` 为永久删除请求显式增加 `[FromBody]`，并补充端点绑定回归测试。
修复后 API `294/294`、完整解决方案 `700/700`，Release 构建零警告、零错误。新制品
先使用独立空数据库、备用回环端口和临时 systemd 单元真实启动，返回 `0.29.0`、
`database=ready`、迁移 `27/27`、`NRestarts=0`；临时进程、端口和数据库全部清理后，
正式制品在 2026-08-08 12:43:40 至 12:43:47 CST 原子上线。

整个过程只重启 `hechao-launcher-api.service`，没有重启或控制 Publisher、Nginx、
Minecraft、Velocity 或两台 ServerControlAgent。失败的远端 release 已在确认不为
`current` 后精确删除；本地失败归档、哈希和 systemd journal 仍保留用于追溯。

## 验证

- TypeScript、Vite 生产构建、Vitest `8/8` 和 Playwright `20/20` 继承功能候选的完整
  前端验证；后端启动修复未改动前端；
- 修复后 API `.NET 294/294`、完整解决方案 `.NET 700/700`，Release 构建零警告、
  零错误；
- 当前 release 为 `/opt/hechao-launcher-api/releases/0.29.0-20260808T043921Z`；
- API `active/running`，PID `2871799`、`NRestarts=0`，仅监听 `127.0.0.1:8090`；
- PostgreSQL `healthy` 且仅监听 `127.0.0.1:5433`；迁移 `27/27`，8 个档案、10 个版本、
  0 个已归档档案和 0 个进行中整合包任务；
- 回环与公网 `/healthz`、`/readyz` 均返回 `0.29.0` 和 `database=ready`；官网、下载页、
  旧 API 域名及后台档案入口均返回 200，公网 `8090` 不可连接；
- 公网 `chunk-ProfilesView.js` 的大小、SHA-256 与 release 完全一致，并包含生命周期、
  归档和恢复界面；
- 成功切换后的 API 与 Publisher warning/error 均为 0；Publisher PID `1459607`、
  Nginx PID `459682` 及两者 `NRestarts=0` 均未变化；
- 两个上传目录、两个临时数据库、备用端口和失败远端 release 已精确清理；根盘仍有
  6,232,313,856 字节可用；
- 现有浏览器管理员会话已过期，只显示“需要管理员身份”，控制台无 warning/error。
  因此没有把登录后的筛选和抽屉点击写成已完成；自动化 `20/20` 与生产静态哈希已经
  覆盖本次前端发布一致性。

## 回滚

当前直接程序回滚目标为：

`/opt/hechao-launcher-api/releases/0.28.7-20260807T072043Z`

迁移 `027` 为加法迁移。当前尚无已归档档案，因此紧急情况下可原子恢复 `0.28.7` 并只
重启 API；一旦任一档案产生归档状态，旧 API 不理解生命周期字段，禁止再回滚到
`0.28.7`。此后必须前滚修复 `0.29.x`，不得删除迁移、审计或发布记录。数据库恢复必须
使用本次同一时点备份并经过单独批准，不能把程序回滚等同于数据库回滚。

结构化证据见
[`evidence/API_0.29.0_PRODUCTION_DEPLOYMENT_2026-08-08.json`](evidence/API_0.29.0_PRODUCTION_DEPLOYMENT_2026-08-08.json)。

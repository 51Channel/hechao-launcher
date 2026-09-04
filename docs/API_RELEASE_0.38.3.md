# 赫朝启动器 API 0.38.3 正式发布

- 发布日期：`2026-09-04`
- 发布目录：`/opt/hechao-launcher-api/releases/0.38.3-20260904T035059Z`
- 制品源码提交：`301ea5b850ccc563163e8c98d9c043c5bc666087`
- 正式标签：`api-v0.38.3`
- 数据库迁移：`36/36`

## 功能

管理员现在可以先创建活动企划，再在整合包准备完成后绑定客户端。未选择客户端时，
企划以独立的未绑定 `Draft` 保存，名称、公告、开放时间、人数和最低称号均可编辑；它可以
归档和恢复，但发布或部署会明确返回 `409 package_binding_required`。

后续绑定已完成整合包时，API 在同一事务中把企划迁入正式服务器目录，保留企划 ID 和
创建时间并递增修订号。已绑定企划不能解除绑定，但仍可改绑其他有效整合包。客户端精确
清单进入 `Production` 前，企划仍不能发布。未绑定草稿不会进入玩家启动器目录。

## 数据兼容

迁移 `036` 新增 `launcher.unbound_activity_plans`，只允许 `Draft` 和 `Archived`。既有
`launcher.servers` 的客户端档案、Minecraft 版本和加载器非空约束均保持不变。

程序回滚到 `0.38.2` 时，未绑定草稿会暂时不可见但不会丢失；重新部署 `0.38.3` 后恢复。
生产上已把迁移后数据库恢复到隔离库，并以正式 `hechao-api` 账号启动旧版 `0.38.2`：
`healthz`、`readyz` 和数据库就绪均通过，warning 及以上日志为 `0`，临时库随后删除。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.38.3-20260904T035059Z-linux-x64.tar.gz` | `47,007,007` 字节 | `C23739F8E88002134FD32206CCDB2D8DA4FD09DD468A4A138BF060816AA5D904` |
| `Hechao.Api` | `105,718,852` 字节 | `E6A94F921F90FD53A955D1919A52244D6E980FDDB9F962CF1F05C2CB76F374D6` |

归档包含 `164` 个条目；PDB、源码映射、环境文件和密钥类文件均为 `0`。生产可执行文件
大小与摘要和本地制品完全一致。

部署前备份为：

- API 配置、systemd、Nginx、Data Protection、清单和当前发布指针：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.38.3-20260904T035059Z`；
  `SHA256SUMS` 覆盖 `6` 项并已逐项复验，清单 SHA-256 为
  `99A4137E2C2D8A49A07D567DD5A6D40947E221035894F8CE6EB7AB5DAC085A16`；
- PostgreSQL：
  `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.38.3-20260904T035059Z.dump`，
  `7,973,354` 字节，SHA-256
  `5C3761FA9E7A09875BCD84A212289011DF49944BFDF9DD71318CB4C172DD7C3D`。

数据库备份已通过 `pg_restore --list`。

## 生产验收

- AdminWeb Vitest `19/19`、Playwright `36/36`、API 默认测试 `386` 项通过、完整解决方案
  `841` 项通过；`2` 项 PostgreSQL 条件测试在默认环境跳过，隔离 PostgreSQL 活动企划
  状态机 `1/1` 通过；
- PowerShell 7 合规 `49/49`、发布溯源 `29/29` 和 `git diff --check` 通过；
- 创建未绑定草稿返回 `201`，编辑、归档、恢复和后续绑定均返回 `200`；修订号按
  `1 -> 2 -> 3 -> 4 -> 5` 递增，绑定前后企划 ID 与创建时间不变；
- 未绑定状态发布和部署均返回 `409 package_binding_required`；绑定后再次解除客户端也
  返回同一拒绝；绑定所用客户端为精确 `Production` 发布，返回 `productionReady=true`；
- 未绑定时数据库形状为 `unbound=1 / servers=0`，绑定后为 `0 / 1`，旧目录的三个非空列
  继续保持 `NOT NULL`；公开活动目录从未出现临时企划；
- 临时企划、正式目录行和 `5` 条测试审计均已清理，最终未绑定草稿为 `0`、既有活动企划
  仍为 `3`，发布、部署和服控队列均为 `0`；
- 商业街与既有企划的稳定字段指纹在部署前后均为
  `F9D69F8275E96D4FB009D64535E14AEF69359A1266BD6D5FDB38A0D069A1F707`；商业街保持
  `Closed / 玩家可见 / r4`，本次发布未改目录策略；
- `current` 指向正式发布目录，服务为 `active/running`，主 PID `1581713`，
  `NRestarts=0`；回环和公网健康、就绪均为 `200`，部署后 warning 及以上日志为 `0`；
- 管理后台活动企划页为 `200`，匿名管理员会话为 `401`，非管理 Host 和 Launcher API
  管理入口为 `404`，公网 `8090` 不可连接；`hechao.world` 跟随 HTTPS 跳转后为 `200`，
  `api.hechao.world` 为 `200`；
- Publisher 与 Nginx PID 分别保持 `2812528`、`412310`。owl5 的 Java PID
  `2948 / 4500 / 5040 / 5160` 和 ServerControlAgent PID `4764` 前后完全一致。

本次只原子切换并重启 `hechao-launcher-api.service`。没有修改或重启 Nginx、Publisher、
Velocity、服控代理，也没有启动、停止、重启或发送命令给任何 Minecraft 服务端。

## 回滚

程序故障时原子切回
`/opt/hechao-launcher-api/releases/0.38.2-20260904T013112Z`，只重启
`hechao-launcher-api.service`。迁移 `036` 和未绑定草稿表保留，不做数据库降级；旧 API
会忽略该表，已绑定企划继续正常工作。

结构化证据见
[`evidence/API_0.38.3_PRODUCTION_DEPLOYMENT_2026-09-04.json`](evidence/API_0.38.3_PRODUCTION_DEPLOYMENT_2026-09-04.json)。

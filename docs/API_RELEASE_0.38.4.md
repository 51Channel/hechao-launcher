# 赫朝启动器 API 0.38.4 正式发布

- 发布日期：`2026-09-04`
- 发布目录：`/opt/hechao-launcher-api/releases/0.38.4-20260904T060203Z`
- 制品源码提交：`fff9bacf284ea93bad7223a04fa1132258da2bb6`
- 正式标签：`api-v0.38.4`
- 数据库迁移：`37/37`

## 功能

活动企划与实际运行服务器现在是两个明确对象。逻辑企划只出现在活动企划后台和玩家活动
目录；承载企划的真实服务器只出现在普通服务器目录和服控面板。运行状态、在线人数、客户端
下载授权、整合包部署和 Velocity 路由均通过企划绑定解析到真实承载槽。

商业街活动的生产绑定为：

- 逻辑企划：`activity-20260905-69e6741d9`；
- 真实承载槽：`minigame-commercial-street`；
- Velocity 后端：`127.0.0.1:25602`。

普通后台不再显示逻辑企划的重复服务器行，玩家目录也不会同时展示真实承载槽。真实槽被
已发布企划占用时，不能通过真实槽 ID 绕过企划门禁或取得旧客户端；Velocity 授权仍以企划
ID 为外部身份，但返回真实槽的 `velocityTarget` 和后端端口。

## 数据兼容

迁移 `037 activity_plan_target_server` 在迁移 `036` 之上增加企划到真实承载服务器的外键，
并为未绑定草稿增加可选目标。迁移只接受合法固定活动槽或就绪的 owl5 独立槽作为承载目标，
不会把普通固定生存服误作活动承载槽。

生产已安装 `btree_gist 1.7`，活动时间排斥约束现在按“承载服务器 + `[开始, 结束)` 时间段”
计算：同一槽不能同时承载两个已发布活动，不同独立槽可以并行开放。迁移后数据库为
`37/37`，迁移 `036` 的既有定义和校验和保持不变。

旧版 `0.38.3` 可以在紧急情况下读取迁移后的数据库，但不理解真实承载槽语义，回滚期间会
重新出现重复目录项。因此程序回滚只用于短时恢复，并须冻结企划和目录写入；迁移 `037`、
扩展和绑定数据均保留，不做数据库降级。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.38.4-20260904T060203Z-linux-x64.tar.gz` | `47,017,068` 字节 | `EC24F6101BA95B8C2FAC5B5D0F631F00F6C89BAEB56792223A0CBE7E9A4A9D55` |
| `Hechao.Api` | `105,745,476` 字节 | `AD3F54B7987249941BDEAA553C9980D38E6FD110A0658C9B1A887480ED2C3707` |

归档包含 `164` 个条目，生产发布目录包含 `159` 个文件；PDB、源码映射、环境文件和密钥类
文件均为 `0`。生产可执行文件大小与摘要和本地制品一致。

部署前备份为：

- API 环境、systemd、Nginx、Data Protection、清单和当前发布指针：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.38.4-20260904T061806Z`；清单
  SHA-256 为
  `8D05DA38C8DA7F619A25A95A81D99A17B289E1A6CC8FCA735E1AB6A9CE2AD2F5`；
- PostgreSQL：
  `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.38.4-20260904T061806Z.dump`，
  `7,932,140` 字节，SHA-256
  `0D500EA4C5038AC385B1EA9935F98C7816B2914988903AEA1D6BFECAC89C47B6`。

数据库备份已通过 `pg_restore --list`，配置备份清单已复验。

## 生产验收

- AdminWeb Vitest `19/19`、Playwright `38/38`、API 默认测试 `394` 项和完整解决方案
  `849` 项通过；默认环境跳过的 `3` 条 PostgreSQL 条件测试已在一次性隔离库中真实通过
  `3/3`；
- PowerShell 7 合规 `49/49`、发布溯源 `29/29`、格式、`git diff --check`、秘密特征和
  构建物扫描均通过；
- 普通后台查询只返回真实槽 `minigame-commercial-street` 一行，逻辑企划为 `0` 行；活动
  企划查询只返回逻辑企划一行，真实槽为 `0` 行；
- 玩家目录显示逻辑企划并隐藏真实槽；公共活动目录只显示
  `activity-20260905-69e6741d9`，客户端下载授权来源也只有该逻辑企划；
- 权限预览解析为
  `activity-20260905-69e6741d9 -> minigame-commercial-street -> 25602`；
- 隔离允许态 Velocity 实测返回 `allowed=true / reason=Allowed`，外部 `serverId` 保持逻辑
  企划，`velocityTarget=minigame-commercial-street`，后端为 `127.0.0.1:25602`；
- 三条正式绑定为 `activity-20260825-a857dc283 -> activity-modular-boss`、
  `activity-20260905-69e6741d9 -> minigame-commercial-street` 和
  `activity-20260905-d083ca4dd -> minigame-commercial-street`；
- 服控操作、控制台命令、活动部署和整合包流水线均为 `0`；既有静态
  `AwaitingReview` 整合包为 `1`；
- `current` 指向正式发布目录，服务为 `active/running`，主 PID `1626629`，
  `NRestarts=0`；迁移 `37/37`、回环和公网健康/就绪均正常，启动以来 warning 及以上日志
  为 `0`；
- 公共活动接口和管理后台页面为 `200`，匿名后台 API 为 `401`，Launcher API 域名下
  `/admin/` 为 `404`，公网 `8090` 不可连接；`hechao.world` 和
  `api.hechao.world/health` 均为 `200`；
- Publisher、Nginx、owl5 ServerControlAgent PID 分别保持 `2812528`、`412310`、`4764`；
  owl5 Java PID `2948 / 4500 / 5040 / 5160` 完全不变，其中 `5040` 为 Velocity。

本次只原子切换并重启 `hechao-launcher-api.service`。没有修改或重启 Nginx、Publisher、
Velocity、服控代理，也没有启动、停止、重启或发送命令给任何 Minecraft 服务端。

## 回滚

程序故障时原子切回
`/opt/hechao-launcher-api/releases/0.38.3-20260904T035059Z`，只重启
`hechao-launcher-api.service`。保留迁移 `037`、`btree_gist` 和全部企划绑定；回滚期间冻结
企划和目录写入，恢复 `0.38.4` 后再解除冻结。

结构化证据见
[`evidence/API_0.38.4_PRODUCTION_DEPLOYMENT_2026-09-04.json`](evidence/API_0.38.4_PRODUCTION_DEPLOYMENT_2026-09-04.json)。

# 天域远征工业季客户端档案 1.0.29 Test 发布

- 发布核验日期：2026-08-23
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 清单生成时间：`2026-08-23T04:00:43.6050526+08:00`
- 发布状态：已上传、已导入、仅 Test `100%`；Gray 与 Production 未分配
- 后台通道修订：Test `r22`
- 构建来源提交：`2ca3420121233a1a8ab492befa7b226486b0ac60`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.29`

## 发布结果

1.0.29 在不可变 1.0.28 基线上新增 Tom's Simple Storage `2.4.1`，客户端与服务端使用
同一 JAR。服务端先完成完整备份、离线部署和 Arclight 冷启动验收，客户端清单随后导入
后台并只切换 Test。导入审计为 `#10580`，Test 通道切换审计为 `#10585`。

用于发布的后台会话由 `#10577` 创建，并通过既有可信设备 `#10578` 完成验证。该会话已
自然到期，2026-08-23 14:16 CST 回读时活跃后台会话为 `0`、活跃可信设备为 `3`；会话
创建后没有会话或可信设备撤销审计，因此没有为了退出后台而误删可信设备。

通道状态为：

- Test：`1.0.29 / 100% / r22`；
- Gray：未分配，`0% / r1`；
- Production：未分配版本，`100% / r1`。

1.0.28 没有暂停、删除或覆盖，继续作为 Test 和服务端的直接协调回滚目标。档案原有
`is_active=false` 隐藏策略没有改变。

## 精确差异

1.0.29 从不可变 1.0.28 客户端源隔离制作，不读取或修改玩家正在使用的游戏目录。

- 1.0.28 为 `4,457` 个逻辑文件，1.0.29 为 `4,458` 个；
- 原有 `4,457` 个文件的路径、大小和 SHA-256 全部不变；
- 新增 `mods/toms_storage-1.21-2.4.1.jar`；
- 删除路径：`0`；
- 同路径内容变化：`0`；
- 逻辑字节：`1,205,115,322`；
- 去重对象：`4,253`；对象字节：`1,205,106,812`。

Tom's Simple Storage JAR 为 `855,813` 字节，SHA-256 为
`BB31B1CA0F6421F2828658B003F552D278B95DAAF827C0F41A6D080ED7E2614F`。该文件标记为
客户端必需，并与服务端 `mods` 目录中的唯一同名 JAR 完全一致。

## 清单、落盘与 OSS

- 本地清单：
  `artifacts/release-work/skyrealm-industrial-1.0.29-distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 生产清单：
  `/var/lib/hechao-launcher-api/manifests/releases/skyrealm-industrial-neoforge-1.21.1/7dc19884a1e52f7ab0dd27827104c70a831d129f2e3d53071fac2d0b9b88a31b.json`；
- 清单大小：`2,025,941` 字节；
- 清单 SHA-256：
  `7DC19884A1E52F7AB0DD27827104C70A831D129F2E3D53071FAC2D0B9B88A31B`；
- 生产权限与属主：`0640 / hechao-api:hechao-api`；
- 签名密钥 ID：`release-2026-07-primary`；
- 正式信任包离线验签、对象闭合和发布校验：通过；
- OSS 首轮新增 `1` 个对象、复用 `4,252` 个对象，上传 `855,813` 字节；
- OSS 第二轮 `4,253` 个对象全部存在，上传 `0` 字节；
- 不可变对象覆盖：`0`。

## API 与服务端状态

生产 API 为 `0.36.1`。2026-08-23 14:14 CST 回读时 `/healthz`、`/readyz` 和数据库均
正常，主进程 PID 为 `525035`，`NRestarts=0`，迁移为 `34/34`。数据库发布行记录为
`1.0.29 / 1,205,115,322 bytes / 4,458 files / NeoForge 21.1.228 / 未暂停`。

owl5 的 `activity-survival` 服务端目录为
`E:\HechaoActivitySlots\activity-survival`。2026-08-23 14:16 CST 最终只读回查为计划任务
`Running`、Java PID `1524`、`127.0.0.1:25600` 单监听、已建立后端连接 `0`；命令行仍通过
`arclight-neoforge-1.21.1-1.0.2-SNAPSHOT-8086b06.jar` 启动。日志确认 Tom's Simple
Storage common/server 配置均已加载，版本检查为 `2.4.1 -> 2.4.1`，服务端
`Done (4.376s)`，Tom's Storage 相关错误为 `0`。

完整离线备份为
`E:\manual-backups\activity-survival-toms-storage-2.4.1-20260822T201140Z`，当前仍存在。
收口回查没有停止或重启服务端，也没有操作其他 Minecraft、Velocity 或服控进程。

## 待验收与回滚

真人验收仍需由启动器完成 1.0.28 到 1.0.29 的授权增量下载后执行：客户端进入
`activity-survival`、打开 Tom's Storage 方块界面、连接相邻库存、存取与搜索物品、多人
同时访问、断线重连和重启后数据保持，并回归既有 15 项天域远征快捷菜单。完成前不得推进
Gray 或 Production。

回滚时先把 Test 通道恢复到 1.0.28，再在正常保存和停服后恢复完整备份
`E:\manual-backups\activity-survival-toms-storage-2.4.1-20260822T201140Z`。不得只回滚客户端
或只回滚服务端。1.0.29 的签名清单和内容寻址对象继续保留，不删除、不覆盖。

结构化证据见
[evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.29_TEST_RELEASE_2026-08-23.json](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.29_TEST_RELEASE_2026-08-23.json)。

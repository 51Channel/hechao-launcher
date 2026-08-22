# 天域远征工业季客户端档案 1.0.27 Test 发布

- 发布核验日期：2026-08-22
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 清单生成时间：`2026-08-22T10:52:02.2068407Z`
- 发布状态：已上传、已导入、仅 Test `100%`；Gray 与 Production 未分配
- 后台通道修订：Test `r20`
- 构建来源提交：`1020ba6f31132605d5bc28283d1e45c50072dcd4`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.27`

## 发布结果

1.0.27 已在服务端 Screen 0.2.8 冷部署并通过协议 `3` 门禁后导入后台。导入审计为
`#10354`，Test 通道切换审计为 `#10355`。用于发布的短时 CLI 管理会话由 `#10353`
创建，并已由 `#10357` 撤销。

通道状态为：

- Test：`1.0.27 / 100% / r20`；
- Gray：未分配，`0% / r1`；
- Production：未分配版本，`100% / r1`。

1.0.26 没有暂停、删除或覆盖，继续作为 Test 的直接回滚目标。

## 精确差异

1.0.27 从不可变 1.0.26 客户端源隔离制作，不读取或修改玩家正在使用的游戏目录。

- 两个档案均为 `4,457` 个逻辑文件；
- `4,456` 个共同文件的路径、大小和 SHA-256 全部不变；
- 删除 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.7.jar`；
- 新增 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.8.jar`；
- 同路径内容变化：`0`；
- 逻辑字节：`1,204,252,765`；
- 去重对象：`4,252`；对象字节：`1,204,244,255`。

新 Screen JAR 为 `971,287` 字节，SHA-256 为
`0050ED8611248B447F7E95205DB62AEFF1E7A5FE7D34ECCF74DEB8DBAC5D23AC`。
它把网络协议从 `2` 升到 `3`；本次已与服务端 0.2.8 成对发布，没有保留半兼容运行状态。

## 清单、落盘与 OSS

- 本地清单：
  `artifacts/release-work/skyrealm-industrial-1.0.27-distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 生产清单：
  `/var/lib/hechao-launcher-api/manifests/releases/skyrealm-industrial-neoforge-1.21.1/e3d85d4068cd9dcd2882df0b365d471b93ce82d6c1c98b89a6ea082da5fc33b5.json`；
- 清单大小：`2,025,536` 字节；
- 清单 SHA-256：
  `E3D85D4068CD9DCD2882DF0B365D471B93CE82D6C1C98B89A6EA082DA5FC33B5`；
- 生产权限与属主：`0640 / hechao-api:hechao-api`；
- 签名密钥 ID：`release-2026-07-primary`；
- 正式信任包离线验签、对象闭合和发布校验：通过；
- OSS 首轮新增 `1` 个对象、复用 `4,251` 个对象，上传 `971,287` 字节；
- OSS 第二轮 `4,252` 个对象全部存在且逐对象校验通过，上传 `0` 字节；
- 不可变对象覆盖：`0`。

## API 与服务端兼容

生产 API 为 `0.36.1`，`/healthz`、`/readyz` 和数据库状态均正常，主进程 PID 为
`525035`，`NRestarts=0`。发布行记录为 `1.0.27 / 1,204,252,765 bytes /
4,457 files / NeoForge 21.1.228 / 未暂停`。

owl5 的 `activity-survival` 已按先服务端、后客户端通道的顺序完成冷更新。服务端唯一
Screen 为 0.2.8，协议 `3`、JAR 大小和 SHA-256 与档案内对象一致；Arclight 启动、
`25600` 单监听、命令树、HechaoEconomy 健康和零玩家门禁均通过。

## 待验收与回滚

真人验收仍需由启动器完成增量更新后执行：14 项快捷菜单、转账成功/失败/未知、TPA 四动作、
返回无闪屏，以及双账号市场上架、购买、下架、待领取、断线、背包竞争、幂等重试和余额
守恒。完成前不得推进 Gray 或 Production。

协议两端必须成对回滚：客户端 Test 恢复到 1.0.26，服务端在正常保存和停服后恢复完整
备份 `E:\manual-backups\activity-survival-screen-0.2.8-20260822T114459Z` 或其中的
Screen 0.2.1。1.0.27 的清单和内容寻址对象继续保留。

Screen 发布记录见
[SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.8.md](SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.8.md)。
结构化证据见
[evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.27_TEST_RELEASE_2026-08-22.json](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.27_TEST_RELEASE_2026-08-22.json)。

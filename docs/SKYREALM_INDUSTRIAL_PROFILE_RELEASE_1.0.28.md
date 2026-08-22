# 天域远征工业季客户端档案 1.0.28 Test 发布

- 发布核验日期：2026-08-22
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 清单生成时间：`2026-08-22T14:47:48.4883471Z`
- 发布状态：已上传、已导入、仅 Test `100%`；Gray 与 Production 未分配
- 后台通道修订：Test `r21`
- 构建来源提交：`e4401c0253563606e9bdab71ec9b0e8915df2069`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.28`

## 发布结果

1.0.28 已在服务端 Screen 0.2.9 冷部署并通过门禁后导入后台。导入审计为 `#10401`，
Test 通道切换审计为 `#10402`。用于发布的短时 CLI 管理会话由 `#10400` 创建，并已由
`#10403` 撤销。

通道状态为：

- Test：`1.0.28 / 100% / r21`；
- Gray：未分配，`0% / r1`；
- Production：未分配版本，`100% / r1`。

1.0.27 没有暂停、删除或覆盖，继续作为 Test 的直接回滚目标。档案原有
`is_active=false` 隐藏策略没有改变。

## 精确差异

1.0.28 从不可变 1.0.27 客户端源隔离制作，不读取或修改玩家正在使用的游戏目录。

- 两个档案均为 `4,457` 个逻辑文件；
- `4,456` 个共同文件的路径、大小和 SHA-256 全部不变；
- 删除 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.8.jar`；
- 新增 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.9.jar`；
- 同路径内容变化：`0`；
- 逻辑字节：`1,204,259,509`；
- 去重对象：`4,252`；对象字节：`1,204,250,999`。

新 Screen JAR 为 `978,031` 字节，SHA-256 为
`295FD4C83962697EA7D0981B4DA40E7430669D9B72C902F1DEBC74C927E7361F`。网络协议继续为
`3`；本次只扩展服务端授权菜单和固定命令，不改变既有协议格式。

## 清单、落盘与 OSS

- 本地清单：
  `artifacts/release-work/skyrealm-industrial-1.0.28-distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 生产清单：
  `/var/lib/hechao-launcher-api/manifests/releases/skyrealm-industrial-neoforge-1.21.1/753f98221520b2a7330775c983133a985cf5db7436769f4a4a9adf7c7abe88fc.json`；
- 清单大小：`2,025,526` 字节；
- 清单 SHA-256：
  `753F98221520B2A7330775C983133A985CF5DB7436769F4A4A9ADF7C7ABE88FC`；
- 生产权限与属主：`0640 / hechao-api:hechao-api`；
- 签名密钥 ID：`release-2026-07-primary`；
- 正式信任包离线验签、对象闭合和发布校验：通过；
- OSS 首轮新增 `1` 个对象、复用 `4,251` 个对象，上传 `978,031` 字节；
- OSS 第二轮 `4,252` 个对象全部存在且逐对象校验通过，上传 `0` 字节；
- 不可变对象覆盖：`0`。

## API 与服务端兼容

生产 API 为 `0.36.1`，2026-08-22 23:35 CST 复核时 `/healthz`、`/readyz` 和数据库均
正常，主进程 PID 为 `525035`，`NRestarts=0`，迁移为 `34/34`。数据库发布行记录为
`1.0.28 / 1,204,259,509 bytes / 4,457 files / NeoForge 21.1.228 / 未暂停`。

owl5 的 `activity-survival` 已按先服务端、后客户端通道的顺序完成冷更新。服务端唯一
Screen 为 0.2.9，协议 `3`、JAR 大小和 SHA-256 与档案内对象一致；Arclight 启动、
`25600` 单监听、命令树和零后端连接门禁均通过。

## 待验收与回滚

真人验收仍需由启动器完成增量更新后执行：15 项快捷菜单、RTP、服主设置主城、普通玩家
权限拒绝、返回主城，以及既有转账、TPA 和双账号玩家市场流程。完成前不得推进 Gray 或
Production。

回滚时客户端 Test 恢复到 1.0.27，服务端在正常保存和停服后恢复完整备份
`E:\manual-backups\activity-survival-screen-0.2.9-20260822T150407Z` 或保留的 Screen
0.2.8 JAR。1.0.28 的清单和内容寻址对象继续保留。

Screen 发布记录见
[SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.9.md](SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.9.md)。
结构化证据见
[evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.28_TEST_RELEASE_2026-08-22.json](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.28_TEST_RELEASE_2026-08-22.json)。

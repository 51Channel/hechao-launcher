# 天域远征工业季第三方屏幕 0.2.9 Test 发布记录

- 发布核验日期：2026-08-22
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.9.jar`
- 网络协议：`3`，与 0.2.8 相同
- 客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.28`
- 通道：Test `100% / r21`；Gray 与 Production 未分配
- 构建来源提交：`e4401c0253563606e9bdab71ec9b0e8915df2069`
- 正式标签：`hechao-economy-screen-v0.2.9`

## 发布结果

Screen 0.2.9 已先部署到 owl5 的
`E:\HechaoActivitySlots\activity-survival`，通过冷启动门禁后，客户端档案 1.0.28 才导入
后台并只切换 Test。短时 CLI 管理会话由审计 `#10400` 创建，清单导入为 `#10401`，Test
通道切换为 `#10402`，会话由 `#10403` 撤销。

Test 当前为 `1.0.28 / 100% / r21`。Gray 保持未分配、`0% / r1`，Production 保持
未分配版本、`100% / r1`。1.0.27 / Screen 0.2.8 没有删除或覆盖，继续作为成对回滚目标。

## RTP 与主城

“天域远征”快捷首页从 14 项扩展为 15 项，新增“随机传送”。玩家也可直接使用 `/rtp`；
服主可在目标位置使用 `/setcity` 修改主城。

- RTP 使用玩家当前维度的世界边界中心与大小；
- 最大随机范围为 `5000` 格，世界边界内缩 `32` 格；
- 可用范围小于 `64` 格时拒绝传送；
- 每名玩家冷却 `60` 秒，命令失败会释放冷却，退出服务器会清理状态；
- 实际落点由原版 `minecraft:spreadplayers` 裁决，客户端不能指定坐标或执行任意命令；
- 返回主城固定调用 `essentialsspawn:spawn`；
- `/setcity` 与 `/hechaomenu setcity` 固定调用 `essentialsspawn:setspawn`，要求权限等级 `2`；
- 使用命名空间命令避免其他插件抢占 `/spawn` 或 `/setspawn` 根命令。

本轮部署没有执行 `/rtp` 或 `/setcity`，因此没有移动玩家，也没有修改现有主城位置。

## 服务端协调部署

部署前确认目标服无玩家，完成世界保存和正常停服。完整离线备份位于：

`E:\manual-backups\activity-survival-screen-0.2.9-20260822T150407Z`

备份包含 `439` 个文件、`365` 个目录、`408,475,079` 字节；逐路径、长度、SHA-256 与
目录集合对照均为零差异。被替换的 0.2.8 JAR 另保留于：

`E:\manual-staging\hechao-economy-screen-0.2.9-20260822\HechaoEconomyScreen-NeoForge-1.21.1-0.2.8.replaced.jar`

旧 JAR 为 `971,287` 字节，SHA-256 为
`0050ED8611248B447F7E95205DB62AEFF1E7A5FE7D34ECCF74DEB8DBAC5D23AC`。

离线替换后按原 Arclight 方式冷启动。2026-08-22 23:39 CST 的只读复核结果：

- 计划任务 `Hechao-Server-activity-survival` 为 `Running`，PID 为 `5056`；
- 启动命令仍为 `java @user_jvm_args.txt -jar arclight-neoforge-1.21.1-1.0.2-SNAPSHOT-8086b06.jar nogui`；
- `127.0.0.1:25600` 只有一个监听，已建立后端连接为 `0`；
- `mods` 中只有一份 Screen JAR，版本、大小和 SHA-256 与发布制品一致；
- 启动日志为 `Done (4.273s)`；
- 命令帮助树存在 `rtp`、`setcity` 与 `hechaomenu`；
- `/heco health` 的 API、Vault、交易权限和交易能力均为 `true`；
- Screen 专属 warning/error 为 `0`。

## 制品与分发验证

- NeoForge Screen 测试：`109/109`，失败和错误为 `0`；
- 连续两次可复现构建的 JAR 均为 `978,031` 字节；
- JAR SHA-256：
  `295FD4C83962697EA7D0981B4DA40E7430669D9B72C902F1DEBC74C927E7361F`；
- 清单 SHA-256：
  `753F98221520B2A7330775C983133A985CF5DB7436769F4A4A9ADF7C7ABE88FC`；
- 清单为 `2,025,526` 字节，生产落盘权限为 `0640`，属主为 `hechao-api:hechao-api`；
- 正式信任包验签、对象闭合和 OSS 全量复核通过；
- 首轮仅上传 0.2.9 JAR：`1` 个对象、`978,031` 字节；第二轮上传 `0` 字节；
- 不可变对象覆盖为 `0`。

## 真人门禁

代码、服务端兼容和 Test 发布已经成立，但仍需完成：

- 启动器真实增量下载 1.0.28；
- 15 项菜单的目视、权限可见性和返回首页；
- 真人 RTP 的正常落点、冷却、失败恢复、世界边界和重连清理；
- 服主设置测试主城、普通玩家拒绝、返回主城到达新位置，以及按运营决定恢复正式主城；
- 既有转账、TPA 和双账号玩家市场门禁。

这些门禁完成前不得推进 Gray 或 Production。

## 成对回滚

需要回滚时：

1. 先把 Test 指针恢复到 1.0.27；
2. 确认目标服无玩家，正常保存并停止；
3. 从完整离线备份恢复服务端，或恢复保留的 Screen 0.2.8 JAR；
4. 冷启动并重新验收唯一 JAR、命令、日志和端口；
5. 保留 1.0.28 的不可变清单和 OSS 对象，不删除或覆盖历史制品。

结构化证据见
[evidence/SKYREALM_ECONOMY_SCREEN_0.2.9_TEST_RELEASE_2026-08-22.json](evidence/SKYREALM_ECONOMY_SCREEN_0.2.9_TEST_RELEASE_2026-08-22.json)。

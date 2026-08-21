# 天域远征工业季第三方屏幕 0.2.7 Test 发布记录

- 发布核验日期：2026-08-21
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.7.jar`
- 网络协议：`2`，服务端命令、权限和经济交易合同未改变
- 客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.26`
- 通道：Test `100% / r19`；Gray 与 Production 未分配
- 构建来源提交：`fe81d4a3a3d5a947fc11487191d2a9957f1f43bf`
- 正式标签：`hechao-economy-screen-v0.2.7`

## 发布结果

Screen 0.2.7 已随不可变客户端档案 1.0.26 导入后台，并只切换到 Test。
导入审计为 `#9935`，Test 通道切换审计为 `#9936`。1.0.25 / Screen 0.2.5
仍保留为 Test 回滚目标；Gray 和 Production 没有分配本版本。

本轮同时核验了支持交易链路的 API 0.36.0 和 HechaoEconomy 0.2.2：

- API `/healthz` 与 `/readyz` 返回 200，数据库状态为 `ready`；
- HechaoEconomy 0.2.2 已部署到 owl5 的
  `E:\HechaoActivitySlots\activity-survival`；
- 该目标当前计划任务为 `Ready`，`25600` 无监听，保持关闭，发布过程没有擅自启动
  Minecraft 服务端。

## 本轮改动

- 天域远征暂停菜单入口按实际按钮尺寸拆分，并避免 Screen 重建时重复插入；
- 菜单动作统一经过服务端一次性会话、动作白名单、权限和限速校验；
- 合法动作才消费会话，非法动作、过期动作和过快重复点击保留可恢复状态；
- 结果页区分成功、失败、超时和未知状态，提供重试或返回首页，不伪造交易成功；
- 返回首页时保持业务 Screen 原位替换，避免先闪回游戏画面或强制鼠标回中；
- 继续保留官方回收、玩家市场浏览/上架/购买/下架/待领取、队伍和设置入口。

详细的修复前后流程、失败状态矩阵和验收顺序见
[SKYREALM_HECHAO_MENU_FLOW_2026-08-20.md](SKYREALM_HECHAO_MENU_FLOW_2026-08-20.md)。

## 制品与验证

- JAR 大小：`937,825` 字节；
- JAR SHA-256：
  `71E3FE9C74BBBF439AC91461E564E43096B7EA7E2D7DB3585CE45FFC8383A658`；
- NeoForge Screen 测试：`91/91`，失败和错误为 `0`；
- Impeccable layout detector：无发现；
- API 自动化：`375` 通过；
- Bukkit 插件测试：`30/30` 通过；
- 隔离 PostgreSQL 玩家市场事务验收：`1/1` 通过，临时数据库和角色清理后为 `0:0`。

## 分发与边界

- 档案清单 SHA-256：
  `66DC6FB9754CE37A0635E8B79FC1DF1B531B6694728245D68B4D236B9A7DA38A`；
- 新 Screen 对象为内容寻址对象，旧对象和旧档案均未覆盖；
- 本次没有修改 Gray 或 Production；
- 本次没有启动、停止或重启 Minecraft、Velocity、Publisher 或服控进程。

## 发布后门禁

代码合同和自动化测试已经成立，因此本版本可以进入 Test 双账号验收；这不等于线上真人
交易已经正式验收。仍需完成：

- 启动器授权下载后，检查快捷首页、回收卡片、玩家市场上架和返回流程；
- 两个真人账号完成上架、搜索/排序、购买、下架、待领取和余额守恒；
- 验证断线恢复、背包竞争、重复提交幂等和失败退回；
- 在上述门禁完成前，不得推进 Gray 或 Production。

## 回滚

客户端回滚只把 Test 指针恢复到 1.0.25，不删除 1.0.26 的清单或 OSS 对象。
服务端插件如需回滚，必须先按服控流程正常保存并停止目标，再使用既有插件备份恢复；
不得热替换运行中的 JAR，也不得删除玩家市场迁移和交易数据。

结构化证据见
[evidence/SKYREALM_ECONOMY_SCREEN_0.2.7_TEST_RELEASE_2026-08-21.json](evidence/SKYREALM_ECONOMY_SCREEN_0.2.7_TEST_RELEASE_2026-08-21.json)。

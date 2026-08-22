# 天域远征工业季第三方屏幕 0.2.8 Test 发布记录

- 发布核验日期：2026-08-22
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.8.jar`
- 网络协议：`3`，客户端与服务端 Screen 已在同一受控窗口成对冷更新
- 客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.27`
- 通道：Test `100% / r20`；Gray 与 Production 未分配
- 构建来源提交：`1020ba6f31132605d5bc28283d1e45c50072dcd4`
- 正式标签：`hechao-economy-screen-v0.2.8`

## 发布结果

Screen 0.2.8 已先部署到 owl5 的
`E:\HechaoActivitySlots\activity-survival`，通过冷启动门禁后，客户端档案 1.0.27 才导入
后台并只切换到 Test。导入审计为 `#10354`，Test 通道切换审计为 `#10355`；用于 CLI
发布的短时管理会话由 `#10353` 创建，并已由 `#10357` 撤销。

Test 当前为 `1.0.27 / 100% / r20`。Gray 保持未分配、`0% / r1`，Production 保持
未分配版本、`100% / r1`。1.0.26 / Screen 0.2.7 没有删除或覆盖，继续作为客户端
Test 回滚目标。

## 服务端协调部署

部署前确认目标服无玩家，完成世界保存和正常停服。完整离线备份位于：

`E:\manual-backups\activity-survival-screen-0.2.8-20260822T114459Z`

备份包含 `438` 个文件、`408,628,287` 字节；部署窗口内逐路径、长度和 SHA-256 对照
为零差异。旧 Screen 0.2.1 仍在备份中，大小 `908,221` 字节，SHA-256 为
`53DDD560994C0AE1A7CBE6C0673E38EECFA79171DACEA519ACB7B2756218873E`。

离线替换后按原 Arclight 方式冷启动：

`java @user_jvm_args.txt -jar arclight-neoforge-1.21.1-1.0.2-SNAPSHOT-8086b06.jar nogui`

发布后验收结果：

- 计划任务为 `Running`，2026-08-22 20:29 CST 实时 PID 为 `8476`；
- `127.0.0.1:25600` 只有一个监听，已建立后端连接为 `0`；
- `mods` 中只有一份 Screen JAR，版本、大小和 SHA-256 均与发布制品一致；
- 启动日志为 `Done (4.140s)`，`/list` 返回 `0/100`；
- `minecraft:help hechaomenu` 返回 `/hechaomenu [economy]`；
- `/heco health` 的 API、Vault、交易权限和交易能力均为 `true`；
- Screen 专属 warning/error 为 `0`。

新旧 `debug.log` 均有相同的 `12` 条 DISTXFORM error 和 `12` 条对应 warning，来源为
`sable.mixins.json` 尝试加载客户端类 `ClientLevel`。该噪声在更新前后完全一致，不是
Screen 0.2.8 引入，也没有阻止服务端正常完成启动，因此未触发回滚。

## 本轮能力

“天域远征”现在提供 14 项服务端授权的快捷操作：个人账户、回收目录、玩家市场、市场上架、
我的挂单、待领取、玩家转账、我的队伍、玩家传送、返回家园、返回主城、返回上次位置、我的
领地和个人设置。

- 玩家转账使用原生输入与二次确认表单；
- 玩家传送覆盖 TPA 发起、邀请、接受和拒绝；
- 表单只有收到绑定当前会话 UUID 与动作 ID 的服务端授权后才可提交；
- 内部授权回执不进入聊天或 ActionBar；
- 等待结果时禁用重复提交，超时进入“结果未知”并要求玩家核对真实状态；
- 最终余额、物品、权限和传送结果仍由 API、HechaoEconomy、SkyrealmCore 与服务端命令树裁决。

完整交互流程见
[SKYREALM_HECHAO_MENU_FLOW_2026-08-20.md](SKYREALM_HECHAO_MENU_FLOW_2026-08-20.md)。

## 制品与分发验证

- NeoForge Screen 测试：`104/104`，失败和错误为 `0`；
- 连续两次可复现构建的 JAR 均为 `971,287` 字节；
- JAR SHA-256：
  `0050ED8611248B447F7E95205DB62AEFF1E7A5FE7D34ECCF74DEB8DBAC5D23AC`；
- Impeccable layout detector：无发现；
- 清单 SHA-256：
  `E3D85D4068CD9DCD2882DF0B365D471B93CE82D6C1C98B89A6EA082DA5FC33B5`；
- 清单为 `2,025,536` 字节，生产落盘权限为 `0640`，属主为 `hechao-api:hechao-api`；
- 正式信任包验签、对象闭合和 OSS 全量复核通过；
- 首轮新增 `1` 个对象、上传 `971,287` 字节，复用 `4,251` 个对象；第二轮
  `4,252` 个对象全部命中，上传 `0` 字节；不可变对象覆盖为 `0`。

## 真人门禁

代码、服务端兼容和 Test 发布已成立，但尚不能宣称真人完整交易验收通过。仍需完成：

- 启动器真实增量下载 1.0.27；
- 14 项菜单的真人目视和权限可见性；
- 转账成功、失败与结果未知；
- TPA 发起、邀请、接受与拒绝；
- 两个真人账号完成上架、搜索、购买、下架和待领取；
- 断线恢复、背包竞争、幂等重试及余额守恒。

这些门禁完成前不得推进 Gray 或 Production。

## 成对回滚

协议 `3` 不允许只回滚客户端或服务端。需要回滚时：

1. 先把 Test 指针恢复到 1.0.26；
2. 确认目标服无玩家，正常保存并停止；
3. 从完整离线备份恢复服务端，或恢复备份内的 Screen 0.2.1 JAR；
4. 冷启动并重新验收唯一 JAR、协议、命令、日志和端口；
5. 保留 1.0.27 的不可变清单和 OSS 对象，不删除或覆盖历史制品。

结构化证据见
[evidence/SKYREALM_ECONOMY_SCREEN_0.2.8_TEST_RELEASE_2026-08-22.json](evidence/SKYREALM_ECONOMY_SCREEN_0.2.8_TEST_RELEASE_2026-08-22.json)。

# 天域远征工业季客户端档案 1.0.26 Test 发布

- 发布核验日期：2026-08-21
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 清单生成时间：`2026-08-20T15:24:14.8578775Z`
- 发布状态：已上传、已导入、仅 Test `100%`；Gray 与 Production 未分配
- 后台通道修订：Test `r19`
- 构建来源提交：`fe81d4a3a3d5a947fc11487191d2a9957f1f43bf`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.26`

## 发布结果

1.0.26 已由管理员后台导入，并将 Test 从 1.0.25 / r18 切换为
1.0.26 / r19。Gray 保持 `0% / r1`，Production 保持未分配版本、
`100% / r1`。1.0.25 没有暂停、删除或覆盖，继续作为 Test 回滚目标。

本次审计记录为导入 `#9935`、通道切换 `#9936`。清单 SHA-256 为
`66DC6FB9754CE37A0635E8B79FC1DF1B531B6694728245D68B4D236B9A7DA38A`。

## 本地清单与对象

- 清单：
  `artifacts/release-work/skyrealm-industrial-1.0.26-distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 清单大小：`2,025,516` 字节；
- 清单 SHA-256：
  `66DC6FB9754CE37A0635E8B79FC1DF1B531B6694728245D68B4D236B9A7DA38A`；
- 逻辑文件：`4,457`；逻辑字节：`1,204,219,303`；
- 去重对象：`4,252`；对象字节：`1,204,210,793`；
- 签名密钥 ID：`release-2026-07-primary`。

档案内的 Screen 制品为
`HechaoEconomyScreen-NeoForge-1.21.1-0.2.7.jar`，大小 `937,825` 字节，
SHA-256 为
`71E3FE9C74BBBF439AC91461E564E43096B7EA7E2D7DB3585CE45FFC8383A658`。

与 1.0.25 相比，逻辑文件数量不变；唯一内容变化是删除 Screen 0.2.5 对象并新增
Screen 0.2.7 对象。其余共同文件逐路径、逐大小和逐 SHA-256 保持不变。新对象上传前
经过内容寻址校验，未覆盖任何不可变对象。

## 分发与通道

- Test：`1.0.26 / 100% / r19`；
- Gray：未分配，`0% / r1`；
- Production：未分配版本，`100% / r1`；
- 旧档案 1.0.25 保留为 Test 回滚目标；
- API 清单和对象继续使用受限下载，匿名对象不作为公开下载入口。

## 验证与运行边界

- 正式信任包离线验签和对象闭合校验：通过；
- API 0.36.0 健康、就绪和数据库状态：通过；
- HechaoEconomy 0.2.2 已部署，但 activity-survival 当前保持停服；
- 本次档案发布没有启动、停止或重启 Minecraft、Velocity、Publisher 或服控进程。

## 待验收与回滚

真人验收仍需由启动器完成增量更新后执行：快捷菜单、回收卡片、玩家市场上架、返回无
闪屏，以及双账号购买、下架、待领取、断线、背包竞争、幂等重试和余额守恒。完成前不得
推进 Gray 或 Production。

客户端回滚只恢复 Test 指针到 1.0.25，不删除 1.0.26 的清单和对象。

结构化证据见
[evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.26_TEST_RELEASE_2026-08-21.json](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.26_TEST_RELEASE_2026-08-21.json)。

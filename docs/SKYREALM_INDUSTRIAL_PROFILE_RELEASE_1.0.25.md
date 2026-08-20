# 天域远征工业季客户端档案 1.0.25 Test 发布记录

- 发布日期：`2026-08-20`
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 清单生成时间：`2026-08-20T13:14:00.2984955+08:00`
- 发布状态：已上传、已导入、仅 Test `100%`；Gray 与 Production 未分配
- 后台通道修订：Test `r18`
- 构建来源提交：`90ad8dd080ba7e9335829a33083d494ed425ec20`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.25`

## 发布结果

`1.0.25` 已由管理员后台导入，并通过正式 API 将 Test 从
`1.0.24 / 100% / r17` 切换为 `1.0.25 / 100% / r18`。Gray 保持未分配
`0% / r1`，Production 保持未分配版本、`100% / r1`。`1.0.24` 没有暂停、删除或覆盖，
继续作为 Test 回滚目标。

本次审计记录为导入 `9675`、通道切换 `9676`。线上回读确认 Test 清单为
`d8ac9c449926897b7a5b9361182d8706f3034565383733362fbffa22341c9f06`，通道修订为 `18`。

## 本地制品

- 清单：`artifacts/skyrealm-industrial-1.0.25/distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 清单大小：`2,025,584` 字节；
- 清单 SHA-256：`D8AC9C449926897B7A5B9361182D8706F3034565383733362FBFFA22341C9F06`；
- 逻辑文件：`4,457`；逻辑字节：`1,204,210,807`；
- 去重对象：`4,252`；对象字节：`1,204,202,297`；
- 签名密钥 ID：`release-2026-07-primary`。

新 Screen 制品为
`HechaoEconomyScreen-NeoForge-1.21.1-0.2.5.jar`，大小 `929,329` 字节，SHA-256 为
`A23AAB577343F2B0709B23FC064C342E5E8448B8A13B0CBC2EEA8ACD67D44F39`。

## 与 1.0.24 的精确差异

发布档案从已验证的 `1.0.24` 内容寻址对象重建为干净源：

- `1.0.24` 与 `1.0.25` 均为 `4,457` 个逻辑文件；
- `4,456` 个共同文件的路径、大小和 SHA-256 全部不变；
- 删除 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.4.jar`；
- 新增 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.5.jar`；
- 同路径内容变化：`0`；
- 删除对象：`5219350D7A10E3005DA4AF10DCA1B3EDC21C877050FF05228C67C7F41D6F386F`；
- 新增对象：`A23AAB577343F2B0709B23FC064C342E5E8448B8A13B0CBC2EEA8ACD67D44F39`。

## 验证与发布验收

- Gradle `clean test build --no-daemon`：`82/82` 通过；
- Impeccable layout detector：无发现；
- 正式信任包离线验签：通过；
- 全部对象大小、SHA-256、URL 和无多余对象闭合校验：通过；
- OSS 发布器：新增对象 `1` 个，已存在并逐哈希校验跳过 `4,251` 个，上传字节
  `929,329`，不可变对象覆盖 `0`；
- 远端清单原始字节：`2,025,584` 字节，SHA-256 与本地一致，属主
  `hechao-api:hechao-api`，权限 `0640`；
- 匿名清单和新 Screen 对象均返回 `401`；
- API `/healthz`、`/readyz` 均为 `200`，数据库 `ready`，systemd 为 `active/running`，
  主 PID `3236646`，`NRestarts=0`，发布窗口 warning 以上日志 `0`；
- 没有启动、停止或重启任何游戏服、Velocity、API、Publisher 或服控进程。

## 发布后门禁

发布后仍需通过启动器增量更新和真人验收确认：`3x2` 快捷首页、回收卡片、玩家市场上架、
无闪屏返回，以及两个真人账号的购买、下架、待领取、断线、背包竞争、幂等重试和余额守恒。
上述门禁完成前不得推进 Gray 或 Production；`1.0.24` 继续保留为回滚目标。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.25_TEST_RELEASE_2026-08-20.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.25_TEST_RELEASE_2026-08-20.json)。

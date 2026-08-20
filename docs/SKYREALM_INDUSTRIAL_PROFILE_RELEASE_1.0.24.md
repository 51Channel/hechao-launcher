# 天域远征工业季客户端档案 1.0.24 Test 发布记录

- 发布日期：`2026-08-20`
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 发布状态：已上传、已导入、仅 Test `100%`；Gray 与 Production 未分配
- 后台通道修订：Test `r17`
- 构建来源提交：`02a9727`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.24`

## 发布结果

`1.0.24` 已由管理员后台导入，并通过正式 API 将 Test 从
`1.0.23 / 100% / r16` 切换为 `1.0.24 / 100% / r17`。Gray 保持未分配
`0% / r1`，Production 保持未分配版本、`100% / r1`。`1.0.23` 没有暂停、删除或覆盖，
继续作为 Test 回滚目标。

本次审计记录为导入 `9645`、通道切换 `9647`。数据库实时回读确认 Test 清单为
`ef965b1795c66cc1cc2396bd87a380f71de3cc185315a811e3889efcaddc046e`，通道修订为 `17`。

## 本地制品

- 清单：`artifacts/skyrealm-industrial-1.0.24/distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 清单大小：`2,025,541` 字节；
- 清单 SHA-256：`EF965B1795C66CC1CC2396BD87A380F71DE3CC185315A811E3889EFCADDC046E`；
- 逻辑文件：`4,457`；逻辑字节：`1,204,209,136`；
- 去重对象：`4,252`；对象字节：`1,204,200,088`；
- 签名密钥 ID：`release-2026-07-primary`。

新 Screen 制品为
`HechaoEconomyScreen-NeoForge-1.21.1-0.2.4.jar`，大小 `927,658` 字节，SHA-256 为
`5219350D7A10E3005DA4AF10DCA1B3EDC21C877050FF05228C67C7F41D6F386F`。

## 与 1.0.23 的精确差异

发布档案从已验证的 `1.0.23` 内容寻址对象重建为干净源：

- `1.0.23` 与 `1.0.24` 均为 `4,457` 个逻辑文件；
- `4,456` 个共同文件的路径、大小和 SHA-256 全部不变；
- 删除 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.3.jar`；
- 新增 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.4.jar`；
- 同路径内容变化：`0`；
- 删除对象：`9C56DBCC357745056FECAB701EC9E3D9E874C8FD64B3AB581839FC262DB72802`；
- 新增对象：`5219350D7A10E3005DA4AF10DCA1B3EDC21C877050FF05228C67C7F41D6F386F`。

## 验证与发布验收

- Gradle `clean test build --no-daemon`：`78/78` 通过；
- 正式信任包离线验签：通过；
- 全部对象大小、SHA-256、URL 和无多余对象闭合校验：通过；
- OSS 发布器：新增对象 `1` 个，已存在并逐哈希校验跳过 `4,251` 个，上传字节
  `927,658`，不可变对象覆盖 `0`；
- 远端清单原始字节：`2,025,541` 字节，SHA-256 与本地一致，属主
  `hechao-api:hechao-api`，权限 `0640`；
- 匿名清单和新 Screen 对象均返回 `401`；
- API `/healthz`、`/readyz` 均为 `200`，数据库 `ready`，systemd 为 `active/running`，
  主 PID `3236646`，`NRestarts=0`，发布窗口 warning 以上日志 `0`；
- 没有启动、停止或重启任何游戏服、Velocity、API、Publisher 或服控进程。

## 发布后门禁

发布后仍需通过启动器增量更新和真人验收确认：账户信息、队伍成员交互、个人设置、上架
页面拖放与价格确认、搜索和双账号市场购买/下架/待领取流程，以及断线、背包竞争和幂等
重试。上述门禁完成前不得推进 Gray 或 Production；`1.0.23` 继续保留为回滚目标。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.24_TEST_RELEASE_2026-08-20.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.24_TEST_RELEASE_2026-08-20.json)。

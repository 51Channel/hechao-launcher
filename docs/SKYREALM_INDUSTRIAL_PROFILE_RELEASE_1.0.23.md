# 天域远征工业季客户端档案 1.0.23 Test 发布记录

- 发布日期：2026-08-19
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 发布状态：已上传、已导入、仅 Test `100%`；Gray 与 Production 未分配
- 后台通道修订：Test `r16`
- 源码提交：`73136ff569e434ef30bc94ff2705b1b819bfa592`

## 发布结果

`1.0.23` 已由 `51Channel / owner` 管理员会话导入后台，并通过正式 API 以
`expectedRevision=15` 将 Test 从 `1.0.22 / 100% / r15` 切换为
`1.0.23 / 100% / r16`。Gray 保持未分配 `0% / r1`，Production 保持未分配
`100% / r1`。`1.0.22` 没有暂停、删除或覆盖，继续作为 Test 回滚目标。

本次审计记录为导入 `9438`、通道切换 `9439`。API `0.35.0` 的数据库回读显示
档案共有 `15` 个不可变版本，`1.0.23` 元数据为 NeoForge `21.1.228`、Java
`21`、`4,457` 个文件。

## 本地制品

- 清单：`artifacts/skyrealm-industrial-1.0.23/distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 清单大小：`2,025,521` 字节；
- 清单 SHA-256：
  `61B9851E9A62C4E799D82CCBBB7E99FD8D64B289FA2F10B0E7ED8AF527732020`；
- 逻辑文件：`4,457`；逻辑字节：`1,204,208,598`；
- 去重对象：`4,252`；对象字节：`1,204,200,088`；
- 签名密钥 ID：`release-2026-07-primary`。

新 Screen 制品为
`HechaoEconomyScreen-NeoForge-1.21.1-0.2.3.jar`，大小 `927,120` 字节，SHA-256 为
`9C56DBCC357745056FECAB701EC9E3D9E874C8FD64B3AB581839FC262DB72802`。

## 精确差异

发布档案从已验证的 `1.0.22` 内容寻址对象重建为干净源，不读取或修改玩家正在使用的实例目录。

- `1.0.22` 与 `1.0.23` 均为 `4,457` 个逻辑文件；
- `4,456` 个共同文件的路径、大小和 SHA-256 全部不变；
- 删除 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.1.jar`；
- 新增 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.3.jar`；
- 同路径内容变化：`0`；
- 删除对象：旧 Screen SHA-256
  `53DDD560994C0AE1A7CBE6C0673E38EECFA79171DACEA519ACB7B2756218873E`；
- 新增对象：新 Screen SHA-256
  `9C56DBCC357745056FECAB701EC9E3D9E874C8FD64B3AB581839FC262DB72802`。

## 验证与发布验收

- Gradle `clean test build --no-daemon`：`78/78` 通过；
- `git diff --check`：通过；
- 正式信任包离线验签：通过；
- 全部对象大小、SHA-256、URL 和无多余对象闭合校验：通过；
- 当前本地玩家实例仍为安装元数据 `1.0.22`，保留玩家设置、存档、日志、下载缓存和 Java；
- OSS 发布器结果：新增对象 `1` 个，已存在并逐哈希校验跳过 `4,251` 个，上传字节
  `927,120`，不可变对象覆盖 `0`；
- 远端清单原始字节：`2,025,521` 字节，SHA-256 与本地一致，属主
  `hechao-api:hechao-api`，权限 `0640`；
- 匿名清单和新 Screen 对象均返回 `401`；
- API `/healthz`、`/readyz` 均为 `200`，数据库 `ready`，systemd 为 `active/running`，
  主 PID `3236646`，`NRestarts=0`，发布窗口 warning 以上日志 `0`；
- 没有启动、停止或重启任何游戏服、Velocity、API、Publisher 或服控进程。

## 发布后门禁

发布后仍需通过启动器增量更新和真人验收确认：账户信息、队伍成员交互、个人设置、上架
页面拖放与价格确认、搜索和双账号市场购买/下架/待领取流程，以及断线、背包竞争和幂等
重试。上述门禁完成前不得推进 Gray 或 Production。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.23_TEST_RELEASE_2026-08-19.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.23_TEST_RELEASE_2026-08-19.json)。

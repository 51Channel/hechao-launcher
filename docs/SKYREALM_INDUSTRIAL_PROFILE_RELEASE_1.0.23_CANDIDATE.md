# 天域远征工业季客户端档案 1.0.23 本地候选

- 候选日期：2026-08-19
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 候选状态：已生成、验签、完成对象闭合；未上传 OSS、未导入后台
- 源码提交：`73136ff569e434ef30bc94ff2705b1b819bfa592`

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

候选从已验证的 `1.0.22` 内容寻址对象重建为干净源，不读取或修改玩家正在使用的实例目录。

- `1.0.22` 与 `1.0.23` 均为 `4,457` 个逻辑文件；
- `4,456` 个共同文件的路径、大小和 SHA-256 全部不变；
- 删除 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.1.jar`；
- 新增 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.3.jar`；
- 同路径内容变化：`0`；
- 删除对象：旧 Screen SHA-256
  `53DDD560994C0AE1A7CBE6C0673E38EECFA79171DACEA519ACB7B2756218873E`；
- 新增对象：新 Screen SHA-256
  `9C56DBCC357745056FECAB701EC9E3D9E874C8FD64B3AB581839FC262DB72802`。

## 验证与门禁

- Gradle `clean test build --no-daemon`：`78/78` 通过；
- `git diff --check`：通过；
- 正式信任包离线验签：通过；
- 全部对象大小、SHA-256、URL 和无多余对象闭合校验：通过；
- 当前本地玩家实例仍为安装元数据 `1.0.22`，保留玩家设置、存档、日志、下载缓存和 Java；
- 没有启动、停止或重启任何游戏服、Velocity、API、Publisher 或服控进程。

正式发布前必须先把 `1.0.23` 导入后台并只分配到 Test，保留 `1.0.22` 作为回滚目标；随后用
启动器增量更新和真人验收确认账户、队伍、设置、出售和双账号市场流程。当前没有执行 OSS
上传、后台导入或 Test 指针切换。

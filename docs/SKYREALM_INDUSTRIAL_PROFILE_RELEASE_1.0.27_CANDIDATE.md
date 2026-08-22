# 天域远征工业季客户端档案 1.0.27 OSS 候选

- 候选核验日期：2026-08-22
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 清单生成时间：`2026-08-22T10:52:02.2068407Z`
- 候选状态：已签名、验签、完成对象闭合并上传 OSS；尚未导入后台或切换通道
- 构建来源提交：`1020ba6f31132605d5bc28283d1e45c50072dcd4`
- 计划标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.27`

## 精确差异

1.0.27 从不可变 `1.0.26` 客户端源隔离制作，不读取或修改玩家正在使用的游戏目录。

- 两个档案均为 `4,457` 个逻辑文件；
- `4,456` 个共同文件的路径、大小和 SHA-256 全部不变；
- 删除 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.7.jar`；
- 新增 `mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.8.jar`；
- 同路径内容变化：`0`；
- 逻辑字节：`1,204,252,765`；
- 去重对象：`4,252`；对象字节：`1,204,244,255`。

新 Screen JAR 大小为 `971,287` 字节，SHA-256 为
`0050ED8611248B447F7E95205DB62AEFF1E7A5FE7D34ECCF74DEB8DBAC5D23AC`。
其网络协议由 `2` 升到 `3`，不能与服务端 Screen `0.2.1` 或客户端 Screen `0.2.7`
混用。

## 清单与 OSS

- 清单：
  `artifacts/release-work/skyrealm-industrial-1.0.27-distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 清单大小：`2,025,536` 字节；
- 清单 SHA-256：
  `E3D85D4068CD9DCD2882DF0B365D471B93CE82D6C1C98B89A6EA082DA5FC33B5`；
- 签名密钥 ID：`release-2026-07-primary`；
- 正式信任包离线验签：通过；
- 对象路径、长度、SHA-256、URL 和无多余对象闭合校验：通过；
- OSS 首轮：新增 `1`，已存在并校验 `4,251`，上传 `971,287` 字节；
- OSS 第二轮：新增 `0`，已存在并校验 `4,252`，上传 `0` 字节；
- 不可变对象覆盖：`0`。

## 实时发布边界

2026-08-22 只读复核时：

- API `0.36.1` 健康、就绪，数据库状态为 `ready`；
- 后台仅存在档案 `1.0.26`，尚无 `1.0.27` 发布记录；
- Test 为 `1.0.26 / 100% / r19`；
- Gray 未分配，`0% / r1`；
- Production 未分配版本，`100% / r1`；
- owl5 的 `activity-survival` 为 `Running`，PID `7452`，`127.0.0.1:25600` 单监听；
- 服务端唯一 Screen 为 `0.2.1`，大小 `908,221` 字节，SHA-256
  `53DDD560994C0AE1A7CBE6C0673E38EECFA79171DACEA519ACB7B2756218873E`；
- 本轮没有启动、停止、重启或热替换 Minecraft，也没有改变任何发布通道。

运行状态和 PID 只是 `2026-08-22T11:07:05.7420572Z` 的快照，执行部署前必须重新核验。

## 协调部署门禁

1. 确认目标服无玩家，执行世界保存并正常停止 `activity-survival`；
2. 完整备份服务端旧 Screen `0.2.1`，离线替换为 `0.2.8`；
3. 冷启动并验收唯一 JAR、协议 `3`、Arclight 启动方式、命令、日志和 `25600` 监听；
4. 导入不可变 `1.0.27` 清单，仅把 Test 从 `1.0.26` 切到 `1.0.27`；
5. Gray 与 Production 保持不变；
6. 两个真人账号验收 14 项菜单、转账、TPA、玩家市场、断线恢复、幂等和余额守恒。

任一步失败时，客户端 Test 保持或回退到 `1.0.26`，服务端恢复已备份的 `0.2.1`，不删除
或覆盖 `1.0.27` 的不可变清单和对象。协议两端必须成对恢复，不能只回滚其中一端。

结构化证据见
[evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.27_OSS_CANDIDATE_2026-08-22.json](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.27_OSS_CANDIDATE_2026-08-22.json)。

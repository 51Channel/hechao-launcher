# 天域远征工业季客户端档案 1.0.30 Test 发布

- 发布日期：`2026-08-24`
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 构建来源：不可变档案 `1.0.29`，只替换 Screen `0.2.9 -> 0.2.10`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.30`

## 精确差异

档案从不可变 `1.0.29` 隔离制作，没有读取或修改玩家现有游戏目录。除 Screen JAR 外，
`4,458` 个共同文件的路径、大小和 SHA-256 全部保持不变；删除路径和其他同路径变化均为
`0`。新增/替换的 Screen 文件为：

`mods/HechaoEconomyScreen-NeoForge-1.21.1-0.2.10.jar`

最终清单统计：

- 逻辑文件：`4,458`；
- 逻辑字节：`1,205,127,929`；
- 去重对象：`4,253`；
- 对象字节：`1,205,119,419`；
- 清单 SHA-256：`2B937DD3CDD166630C531C8A3396F106B76F981474EA25480E132D903E15A88`。

## 分发与通道

Test 已切换到 `1.0.30 / 100% / r23`。Gray 保持 `0% / r1` 未分配，Production 保持
`1.0.29 / 100% / r2`。OSS 首轮只新增 `1` 个对象，第二轮校验上传 `0`，不可变对象覆盖
为 `0`。

## 服务端兼容

目标仍为 owl5 的 `activity-survival`，服务端 Screen `0.2.10`、HechaoEconomy `0.2.4`
和 Tom's Simple Storage `2.4.1` 与客户端档案配套。只读回查显示计划任务 `Running`、
`127.0.0.1:25600` 单监听；本轮收口没有启动、停止或重启游戏服。

## 待验收与回滚

小规模测试前仍需真实启动器增量下载、进入服务端、RTP 安全落点、官方商城购买/领取、
Tom's Storage 存取搜索、多人并发、断线重连、重启持久化和既有快捷菜单回归。全部通过前
不得推进 Gray 或 Production。

回滚时先恢复 Test `1.0.29`，再按维护规程恢复 Screen `0.2.9`、经济插件 `0.2.3` 和
对应服务端备份。`1.0.30` 清单和对象继续保留，不删除、不覆盖。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.30_TEST_RELEASE_2026-08-24.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.30_TEST_RELEASE_2026-08-24.json)。

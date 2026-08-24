# 天域远征工业季第三方屏幕 0.2.10 Test 发布

- 发布日期：`2026-08-24`
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.10.jar`
- 网络协议：`3`，与 `0.2.9` 兼容
- 构建来源提交：`9e7a54d46f69f583c696095ad83394c3f012955f`
- 正式标签：`hechao-economy-screen-v0.2.10`
- 配套客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.30`

## 本版变更

RTP 仍保持最大范围 `5000`、边界内缩 `32`、最小范围 `64` 和每名玩家 `60` 秒冷却，
但不再把危险坐标直接交给 `spreadplayers`。服务端线程会筛选支撑方块、脚部和头部空间、
流体、基岩、岩浆、火焰及碰撞箱；找不到安全位置时释放冷却并返回失败，不把玩家送到最后
一个危险候选点。范围、半径和冷却规则没有缩小或放宽。

## 制品与服务端状态

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoEconomyScreen-NeoForge-1.21.1-0.2.10.jar` | `990,638` 字节 | `601A077D267CD6794B7D8DBF2C40975B08BF1A3E4094014B69D151A86A2345A6` |

服务端路径：
`E:\HechaoActivitySlots\activity-survival\mods\HechaoEconomyScreen-NeoForge-1.21.1-0.2.10.jar`。
owl5 只读回查通过：计划任务为 `Running`，`127.0.0.1:25600` 单监听，服务端目录中唯一
Screen JAR 与本制品一致。本轮收口没有重新启动 Minecraft 服务端。

## 通道与验证

- Test：`1.0.30 / 100% / r23`；
- Gray：未分配，`0% / r1`；
- Production：继续 `1.0.29 / 100% / r2`；
- Screen Gradle：`113/113` 通过，失败 `0`，`clean test build` 通过；
- 客户端清单和内容寻址对象已完成闭合校验，首轮新增对象 `1` 个，第二轮上传 `0`，
  不可变对象覆盖 `0`；
- 真实玩家 RTP、商城、Tom's Storage 和既有快捷菜单回归仍属于 Test 验收，不以自动构建
  通过代替真人门禁。

## 回滚

先把 Test 指针恢复到 `1.0.29`，再在维护窗口内恢复 `0.2.9` Screen 和对应服务端状态。
既有完整回滚点为
`E:\manual-backups\activity-survival-toms-storage-2.4.1-20260822T201140Z`；不得删除或
覆盖 `0.2.10` 的 JAR、清单或内容寻址对象。

结构化证据见
[`evidence/SKYREALM_ECONOMY_SCREEN_0.2.10_TEST_RELEASE_2026-08-24.json`](evidence/SKYREALM_ECONOMY_SCREEN_0.2.10_TEST_RELEASE_2026-08-24.json)。

# 天域远征工业季第三方屏幕 0.2.0 Test 发布

- 发布日期：`2026-08-18`
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.2.0.jar`
- 正式标签：`hechao-economy-screen-v0.2.0`
- 网络协议：`2`
- 客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.20`
- 通道：`Test=r13 / 100%`，Gray 与 Production 未分配

## 客户端功能

首页新增玩家市场入口。玩家可在原生模组 UI 内浏览挂单、查看购买确认、放入物品并填写
整组总价、管理自己的挂单以及领取成交或退回物品。回收目录、玩家市场、我的挂单和待
领取都有返回按钮与模糊搜索框，输入约 `0.4` 秒后刷新结果。

搜索支持中文本地化显示名、完整或部分物品 ID、命名空间及卖家名，也支持有序字符匹配。
客户端只负责显示和发送受限请求，不缓存权威余额、价格、库存或权限。

## 制品与验证

- JAR 大小：`894,611` 字节；
- SHA-256：`294A5CBE839448E3A6777F5BF0C7051D8158E552516D53E14B5E7EB723E61BE3`；
- Gradle 测试：`58/58`；
- 与服务端插件 `0.2.0`、API `0.35.0` 的协议合同测试通过。

发布过程没有中断正在运行的客户端，也没有重启 API、Minecraft、Velocity 或服控代理。
玩家退出旧游戏并由启动器增量更新后才会加载该 JAR。

## 待验收

两个真人账号仍需完成上架、中文与 ID 模糊搜索、购买、下架、领取、断线恢复、背包空间
竞争和重复点击。完成前只保留 Test 通道，不推进 Gray 或 Production。

结构化证据见
[`evidence/SKYREALM_ECONOMY_SCREEN_0.2.0_TEST_RELEASE_2026-08-18.json`](evidence/SKYREALM_ECONOMY_SCREEN_0.2.0_TEST_RELEASE_2026-08-18.json)。

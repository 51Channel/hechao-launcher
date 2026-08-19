# 天域远征工业季 HechaoEconomy 0.2.1 生产发布

- 发布日期：`2026-08-19`
- 目标：`activity-survival`
- 服务端目录：`E:\HechaoActivitySlots\activity-survival`
- 正式标签：`hechao-economy-v0.2.1`
- JAR：`HechaoEconomy-0.2.1.jar`

## 上线功能

个人账户查询现在分别返回可用余额与冻结余额。客户端 Screen `0.2.1` 使用两项权威值显示
可用、冻结和总资产；插件不改变账户、账本、托管、税费或市场交易规则，也不在客户端
缓存权威余额。

该变更补齐个人账户页面的信息密度，并为市场、我的挂单和待领取入口提供稳定的账户响应。
所有余额与市场写操作继续由 API `0.35.0` 和服务端插件裁决。

## 制品与部署

- 源码提交：`2ca51f09338c52f412a6ab26112a01da591e11af`；
- JAR 大小：`440,200` 字节；
- SHA-256：`B9F41A0559C6F2EFC7925451B8E2EEDABD8C0AAE17D9A8F7C511F42B7867E395`；
- Gradle 测试：`28/28`；
- 完整离线备份：
  `E:\manual-backups\activity-survival-economy-screen-0.2.1-20260819T033107Z`。

部署前在线玩家为 `0`。工业季正常保存并停止、确认 Java 进程退出后才替换插件；插件目录
只保留一份权威 HechaoEconomy JAR。恢复服务继续使用既有 Arclight 启动方式，没有热替换
运行中的 JAR，也没有操作其他 Minecraft 后端。

## 运行验收

2026-08-19 12:24 CST 回读时，计划任务为 `Running`，唯一监听为
`127.0.0.1:25600 / PID 7436`。Arclight、插件加载、插件启用和 `Done` 均恰好一次，
`NoSuchMethodError` 为 `0`。`/heco health` 显示 API、Vault、命令权威和交易开关均为
`true`，隔离交易为 `0`。

当前 PID 只代表发布后快照，后续运行状态仍需实时复核。玩家侧个人账户排版、冻结余额
显示和三个快捷入口仍需在启动器更新到 Screen `0.2.1` 后做真人目视验收；完成前不得推进
客户端 Gray 或 Production。

## 回滚

回滚必须先正常保存并停止 `activity-survival`，从上述完整离线备份恢复插件目录并校验
文件，再使用既有 Arclight 启动方式恢复。不得热替换，不得删除迁移 034 的市场数据。

结构化证据见
[`evidence/SKYREALM_ECONOMY_0.2.1_PRODUCTION_DEPLOYMENT_2026-08-19.json`](evidence/SKYREALM_ECONOMY_0.2.1_PRODUCTION_DEPLOYMENT_2026-08-19.json)。

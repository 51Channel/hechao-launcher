# 天域远征工业季经济插件 0.2.4 Test 配套发布

- 发布日期：`2026-08-24`
- 组件：`HechaoEconomy-0.2.4.jar`
- 源码提交：`9e75d5ef0dac5a189f9fd2891620042f7bd6c0ce`
- 正式标签：`hechao-economy-v0.2.4`
- 目标服务端：`owl5 / activity-survival`
- 配套 API：`0.37.0-20260823T182444Z`
- 配套 Screen：`0.2.10`

## 功能范围

本版把默认运行身份改为 `activity-survival`，并包含官方商城的 `/shop`、购买确认、待领取
和背包容量检查流程。每个独立服务端仍必须在自己的
`plugins/HechaoEconomy/config.yml` 中设置唯一 `server-id`；令牌缺失或配置非法时继续
fail-closed，不会绕过 API 身份校验。

官方商城、服务器回收目录和玩家市场分别使用 `/shop`、`/prices` 和 `/ah`，商品、余额、
购买和领取均按 `serverId` 隔离。

## 制品与部署

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoEconomy-0.2.4.jar` | `480,903` 字节 | `E403DA7349D8AFE105D3B728743A30D92C235AC3C69EC346063E25A00ECEF28E` |

部署路径：
`E:\HechaoActivitySlots\activity-survival\plugins\HechaoEconomy-0.2.4.jar`。
本版使用新文件名，不覆盖不可变的 `0.2.3` 对象。

## 验证

- HechaoEconomy Gradle：`39/39` 通过；
- `clean test build --no-daemon` 通过，JAR 大小和 SHA-256 已复核；
- owl5 只读回查：计划任务 `Hechao-Server-activity-survival` 为 `Running`，
  `127.0.0.1:25600` 单监听；
- `/heco health`、API、Vault、命令所有权和交易门禁通过；
- 本轮收口没有执行服务端启停或重启操作；
- 该发布只服务于 Test 档案，不推进 Gray 或 Production。

## 回滚

将 Test 档案恢复到 `1.0.29`，并在维护窗口内把目标服插件恢复到 `0.2.3`；完整服务端回滚
点为既有
`E:\manual-backups\activity-survival-toms-storage-2.4.1-20260822T201140Z`。
不得删除数据库中的商城、市场、账户或交易历史，也不得覆盖 `0.2.4` 不可变制品。

结构化证据见
[`evidence/SKYREALM_ECONOMY_0.2.4_TEST_RELEASE_2026-08-24.json`](evidence/SKYREALM_ECONOMY_0.2.4_TEST_RELEASE_2026-08-24.json)。

# 天域远征工业季经济插件 0.2.4 候选记录

- 记录日期：`2026-08-23`
- 组件：`HechaoEconomy-0.2.4.jar`
- 状态：本地配置安全候选，尚未部署
- 当前线上基线：`HechaoEconomy 0.2.3`
- 源码提交：`9e7a54d46f69f583c696095ad83394c3f012955f`

## 修复范围

插件资源模板原先默认使用旧服标识 `skyrealm`。新部署若直接保存默认配置，会以错误身份
请求经济 API，最终得到 `403` 并在游戏内表现为空商城。`0.2.4` 将当前目标默认值改为
`activity-survival`，同时更新 fail-closed 回退身份和配置测试。

部署到其他独立槽时，仍必须在该服自己的 `plugins/HechaoEconomy/config.yml` 中覆盖
`server-id`；不同服务端不能共用另一个服的身份或运行配置。令牌缺失或配置非法时，交易
继续 fail-closed，不会绕过 API 身份校验。

线上 `activity-survival` 已通过热重载恢复正确配置，生产接口返回 HTTP `200` 和 `85`
个启用商品，因此本候选不需要为恢复当前商城而立刻冷更新。正式部署必须使用新的
`0.2.4` 文件名，不能覆盖不可变的 `0.2.3` 制品。

## 构建证据

- PowerShell 7、Java 21、Gradle 构建：通过；连续两次 `clean test build --no-daemon` 产物一致；
- HechaoEconomy 测试：`37/37`，失败 `0`，错误 `0`；
- JAR：`446,655` 字节；
- SHA-256：`886E21CFF52A6DE25FAF3ECBAEEA77E2CE2870979A26970E55C90D81EF87FF29`；
- `git diff --check`：通过。

## 发布门禁

- PowerShell 7 干净构建和全部 HechaoEconomy 测试通过；
- 记录最终 JAR 大小、SHA-256 与源码提交；
- 等明确维护窗口，确认零玩家、保存世界、完整备份后冷替换；
- 启动后验证唯一插件 JAR、`/heco health`、HTTP `200 / 85` 和现有市场交易；
- 真人确认 `/shop`、官方回收、上架、购买、下架与待领取。

回滚时恢复部署前完整备份中的 `HechaoEconomy-0.2.3.jar` 和配置，不删除数据库中的商品、
账户、挂单或历史交易，也不覆盖候选制品。

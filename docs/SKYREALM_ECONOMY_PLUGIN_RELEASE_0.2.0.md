# 天域远征工业季 HechaoEconomy 0.2.0 生产发布

- 发布日期：`2026-08-18`
- 目标：`activity-survival`
- 服务端目录：`E:\HechaoActivitySlots\activity-survival`
- 正式标签：`hechao-economy-v0.2.0`
- JAR：`HechaoEconomy-0.2.0.jar`

## 上线功能

`/ah` 现在提供玩家市场、放入式上架、我的挂单、购买、下架和待领取。玩家市场、我的
挂单、待领取和完整官方回收目录支持中文显示名、物品 ID、命名空间及卖家名模糊搜索；
回收目录先过滤完整服务端会话再分页。

所有余额修改、托管、成交税和交付状态仍由 API 与服务端插件裁决。服务端限制搜索长度、
物品 ID 格式和映射数量，搜索请求本身不能修改余额、发放物品或提升权限。

## 制品与部署

- JAR 大小：`440,154` 字节；
- SHA-256：`43D94F92786D79FA5B4F385C32AF725CBA75587C8DE6CF649BE24D8481664522`；
- Gradle 测试：`27/27`；
- 完整离线备份：
  `E:\manual-backups\activity-survival-economy-0.2.0-20260818T122955Z`。

部署时在线玩家为 `0`。工业季先正常保存并停止，确认进程退出后才替换插件；插件目录只
保留一份权威 HechaoEconomy JAR，随后继续使用既有 Arclight 启动方式恢复服务。没有热
替换运行中的 JAR，也没有操作其他 Minecraft 后端。

## 验收与边界

部署后快照 PID 为 `2148`；`/heco health` 中 API、Vault、命令权威和交易开关均为
`true`，隔离交易为 `0`。该 PID 仅是部署后快照，后续运行状态仍需实时复核。

两个真人账号的上架、模糊搜索、购买、下架、领取、断线恢复、背包竞争、幂等重试和余额
守恒仍未完成。因此当前状态是“服务端生产部署完成、客户端只进入 Test、真人交易待
验收”，不是全量玩家正式开放。

## 回滚

回滚必须先正常保存并停止 `activity-survival`，从上述完整离线备份恢复插件目录并校验
文件，再使用既有 Arclight 启动方式恢复。不得热替换，也不得删除迁移 034 的市场数据。

结构化证据见
[`evidence/SKYREALM_ECONOMY_0.2.0_PRODUCTION_DEPLOYMENT_2026-08-18.json`](evidence/SKYREALM_ECONOMY_0.2.0_PRODUCTION_DEPLOYMENT_2026-08-18.json)。

# 天域远征工业季回收目录 v2 生产发布

## 发布结果

- 日期：`2026-08-18`
- 目标：`activity-survival`
- 商品：`85` 项，全部启用
- 写入前：`0` 项
- 写入后：`85` 项启用，逐字段验证 `85/85`
- 二次预览：`0` 项差异
- 源清单 SHA-256：
  `EECC26232421BBDDCBB8D6483FC27DBF185153EC5366F6E827DFC405FD1B5ED4`
- 服务端插件：HechaoEconomy `0.1.6`
- 服务端 JAR SHA-256：
  `13069366685FAB4BE15BE0F362F8B491727DA6284C8D14C3DCAB099F3C63E315`
- 客户端：Screen `0.1.9`，档案 `skyrealm-industrial-neoforge-1.21.1 / 1.0.18`
- 客户端清单 SHA-256：
  `9BE857DAEAD9743D79C96F04917E4040B5796153A9BA5C91826E3B51809562EB`
- 客户端 JAR SHA-256：
  `CAB56EE1062402D72581EE9290C4975DBBBEEFF2F22013F6BD5B585B03358691`
- 发布通道：`Test=r11 / 100%`；Gray 与 Production 未分配

发布使用
[`Set-HechaoEconomyCatalog.ps1`](../tools/server/Set-HechaoEconomyCatalog.ps1)
从权威 Markdown 表解析商品，不在脚本中复制第二份价格。工具默认 `Validate`，支持
`Preview / Apply / Disable`；`Apply` 会先快照现有目录，单项失败时恢复已经写入的项目，
完成后再次逐字段回读。

商品写入只调用现有 Economy API，写入阶段没有替换运行中的 JAR，也没有重启 Minecraft、
Velocity、API 或代理。随后为上线完整分页和修复 Arclight 下商品管理按钮的 Adventure
类缺失，单独执行了一次受管 Minecraft 冷更新；API、Velocity 和服控代理均未重启。

## 冷更新与备份

1. 受管 `list` 返回 `0/100`，随后 `save-all flush` 明确返回 `Saved the game`。
2. 使用结构化“停止”操作正常关闭三个维度；旧 PID `7708` 消失，任务进入 `Ready`，
   `25600` 不再监听。
3. 完整离线备份位于
   `E:\manual-backups\activity-survival-economy-0.1.6-20260818T151016`。源目录的 `413`
   个文件、`405,956,639` 字节均按相对路径、长度和 SHA-256 与备份逐项一致；清单摘要为
   `5C5341B0B65D4F29244AB6CDCB671AA9975C39732C81F50A98FCE840BC1D925D`。
4. 先前未生成归档的 VSS 尝试不计为成功备份；其单个残留快照已精确删除，5 个状态文件
   归档到 `C:\ProgramData\Hechao\WorldBackup\failed\activity-survival-20260818T151119`。
5. 旧 `0.1.5` JAR 以原摘要
   `304794280382DFC6002D316C41ABA7B07602A7C84F29BE443FC9D7A6271ABF47` 保留在离线回滚目录，
   `0.1.6` 经暂存、摘要校验和同卷重命名后成为唯一生产 HechaoEconomy JAR。

## 生产验收

- 计划任务为 `Running`，新 PID `6184` 使用 `E:\jdk\bin\java.exe`，命令继续经过
  `arclight-neoforge-1.21.1-1.0.2-SNAPSHOT-8086b06.jar`；`25600` 只有一个监听者。
- 日志中的 `Running on Bukkit - Arclight`、`Done`、HechaoEconomy `0.1.6` 加载和启用
  均恰好出现一次；启动窗口内 Economy 异常和 Adventure `TextColor` 类缺失均为 `0`。
- `/heco health` 返回：API 配置、Vault 权威、命令权威、可交易全部为 `true`，隔离交易
  为 `0`；PlaceholderAPI expansion 注册为 `0.1.6`。
- 公网 API `0.33.1` 的 `/healthz=ok`、`/readyz=ready`，数据库为 `ready`。
- 服务端商品容器按 `45 + 40` 分两批；客户端能跨服务器批次并在批次内继续分页，因此
  85 项均可浏览。服务端 `21/21`、客户端 `26/26` 测试及两端 `clean test build` 通过。

脱敏发布证据：
[`SKYREALM_ECONOMY_CATALOG_V2_PRODUCTION_2026-08-18.json`](evidence/SKYREALM_ECONOMY_CATALOG_V2_PRODUCTION_2026-08-18.json)
和
[`SKYREALM_ECONOMY_CATALOG_V2_DEPLOYMENT_2026-08-18.json`](evidence/SKYREALM_ECONOMY_CATALOG_V2_DEPLOYMENT_2026-08-18.json)。

## 回滚

需要紧急停止全表回收时，在 owl5 使用同一权威清单、现有外置服务身份和发布工具执行
`-Mode Disable`。该操作只把 85 项标记为停用，不删除商品、账本或审计记录；完成后必须
回读 `85/85` 项均为停用。再次启用使用 `-Mode Apply`，价格和额度恢复为本表值。

若 `0.1.6` 运行异常，先通过结构化服控保存并停止 `activity-survival`，再从上述完整离线
备份或其 `rollback\HechaoEconomy-0.1.5.jar` 恢复旧 JAR，校验旧摘要后通过结构化按钮
启动。不得在 Java 进程运行时热替换插件。

## 已知风险

本次按照明确要求全量启用。当前单商品个人/全服数量额度有效，但个人跨商品
`5,000.00`、全服跨商品 `100,000.00` 金额门禁、北京时间额度日和超额部分数量回收尚未
实现。现阶段单人和全服理论最高发币量分别为 `11,056.00` 与 `221,120.00` 金币/日；
应继续监控自动化产线成交量，并优先补齐总额门禁。

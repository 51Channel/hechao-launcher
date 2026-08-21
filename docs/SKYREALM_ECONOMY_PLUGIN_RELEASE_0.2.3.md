# 天域远征 HechaoEconomy 0.2.3 正式发布

- 发布日期：`2026-08-21`
- 源码提交：`9b00be37100548274da611454f1c59a791dfdf27`
- 正式标签：`hechao-economy-v0.2.3`
- 服务端目录：`E:\HechaoActivitySlots\activity-survival`
- 配套 API：`0.36.1-20260821T083823Z`
- 客户端：保持 Screen `0.2.7`、档案 `1.0.26 / Test r19`

## 功能范围

出售菜单继续校验原始整组快照，但只把 API 报价数量放入服务端托管。超出额度的余量留在
输入槽；成功时显示本次回收和保留数量，明确失败时把托管数量与槽内余量合并恢复，结果
未知时只隔离报价数量。网关同时解析 API 结构化错误码，分别提示商品暂停、个人今日额度
已用完和全服今日额度已用完。

## 制品与备份

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoEconomy-0.2.3.jar` | 446,608 字节 | `87ACFC0F23564BE3773D2CEB080CC2AEBC8DA8A8E68C24A0841DDEAC8FED80CA` |

零玩家停服前执行了 `save-all flush`，并在端口和 Java 进程完全退出后制作完整离线备份：

`E:\manual-backups\activity-survival-economy-0.2.3-20260821T085846Z`

备份共 `434` 个文件、`408,019,578` 字节，源与备份逐路径、长度和 SHA-256 差异为 `0`。
旧 JAR 另保留在
`C:\Users\Administrator\HechaoEconomy-0.2.2.jar.rollback-20260821T0900Z`，用于快速回滚。

## 验证

- PowerShell 7 + Java 21：HechaoEconomy `37/37` 通过；
- 连续两次 `clean test build --no-daemon` 产物大小和 SHA-256 完全一致；
- 生产插件目录只有一份 `0.2.3` JAR，远端大小和 SHA-256 与本地原件一致；
- 计划任务最终为 `Running`，PID `7452`，`127.0.0.1:25600` 单监听；
- 启动命令保持 Arclight：
  `java @user_jvm_args.txt -jar arclight-neoforge-1.21.1-1.0.2-SNAPSHOT-8086b06.jar nogui`；
- 当前启动日志中 `Done` 和 HechaoEconomy `0.2.3` 启用均恰好一次，插件相关
  `ERROR/SEVERE=0`；
- `/heco health` 的 API、Vault、命令所有权与交易门禁均为 `true`，隔离记录为 `0`；
- 最终在线人数 `0/100`；配套 API 的生产 `64 -> 32` 报价烟测及测试数据清理通过。

没有重新发布客户端或改变 Test、Gray、Production 指针。由于维护窗口没有真人在线，确认
按钮后的槽位视觉结果仍需玩家下次实际回收时目视验收；数量拆分、失败合并和结果未知隔离
路径已由自动测试覆盖。

## 回滚

先结构化停止 `activity-survival`，恢复 `0.2.2` JAR，同时把 API 切回
`0.36.0-20260820T145340Z`，再用原 Arclight 计划任务启动并复核健康。不得只回滚其中一端。
必要时可从完整离线备份恢复目标服；不操作其他 Minecraft、Velocity、Publisher 或 Nginx。

结构化证据见
[`evidence/SKYREALM_ECONOMY_0.2.3_PRODUCTION_DEPLOYMENT_2026-08-21.json`](evidence/SKYREALM_ECONOMY_0.2.3_PRODUCTION_DEPLOYMENT_2026-08-21.json)。

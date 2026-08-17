# 天域远征工业季经济插件 0.1.5 发布

- 发布日期：2026-08-17
- 源码提交：`5febdd0`
- 正式标签：`hechao-economy-v0.1.5`
- Bukkit 插件：`HechaoEconomy 0.1.5`
- NeoForge 双端模组：`HechaoEconomyScreen 0.1.3`
- 客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.12 / Test=100%`
- 最终服务器状态：停止

## 部署制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoEconomy-0.1.5.jar` | 371,723 字节 | `304794280382DFC6002D316C41ABA7B07602A7C84F29BE443FC9D7A6271ABF47` |
| `HechaoEconomyScreen-NeoForge-1.21.1-0.1.3.jar` | 27,450 字节 | `68891BD42A91F12036DC18F147DB9576EFAAD5F6B695A8E501CFAAF827F638E3` |

Arclight 启动脚本保持 SHA-256
`29C9DA5BC508B666A54632BB0A968BE930EE770CA61ECEF300B1E2B39F69E976`，令牌、配置、世界和
Velocity 未被替换。

最终回滚点为
`E:\manual-backups\activity-survival-economy-0.1.5-final-20260817T031824Z`；此前 `0.1.3`、
`0.1.4` 的独立回滚点也继续保留。回滚目录只含 JAR，不含经济令牌。

## 真实验收

- Essentials 先启用，HechaoEconomy `0.1.5` 后启用；
- `money`、`balance`、`bal`、`pay`、`sell`、`shop`、`heco` 均归 HechaoEconomy；
- `/heco health` 显示 API、Vault、命令归属和可交易均为 `true`；
- PlaceholderAPI 成功注册 `hechao 0.1.5`；
- Screen `0.1.3` 与协议 `2` 保持一致；
- 商品目录以当前服务身份返回 `200`，当前没有正式商品；
- 无 `NoSuchMethodError`、命令冲突、Vault 冲突或 HechaoEconomy 错误；
- 验收期间无玩家，停止时正常保存主世界、下界和末地；
- 最终计划任务 `Ready`，`25600` 无监听，目标进程为 `0`。

商品目录为空不代表故障。服主确认正式货币价格和额度后，在游戏内手持普通物品执行
`/heco product` 配置；第一批价格不得由部署脚本自动猜测。

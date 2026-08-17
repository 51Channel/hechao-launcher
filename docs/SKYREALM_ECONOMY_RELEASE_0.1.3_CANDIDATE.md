# 天域远征工业季经济组件 0.1.3 候选

- 候选日期：2026-08-17
- Bukkit 插件：`HechaoEconomy 0.1.3`
- NeoForge 双端模组：`HechaoEconomyScreen 0.1.3`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- Java：`21`
- 生产状态：尚未部署

## 修复与功能

- 修复 `/heco product` 无法打开服主快捷商品设置；
- `/heco menu` 对普通玩家可用，管理命令仍要求 `hechao.economy.admin`；
- LuckPerms 非 OP 管理员不再被 NeoForge 层错误隐藏，权限最终由 Bukkit 裁决；
- 点击命令和屏幕经济动作统一使用 `hechaoeconomy:` 命名空间；
- 屏幕协议 `2` 不再接受服务端自定义标题、说明和按钮文案；
- 会话绑定 action 白名单、单击消费、过期拒绝、跨玩家拒绝和 `350ms` 限速；
- 余额异步预热与单飞刷新，TAB 高频 Placeholder 不阻塞主线程；
- 转账结果未知时以同一幂等键重试一次；
- Vault、命令所有权、API 或令牌异常时新交易故障关闭。

## 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoEconomy-0.1.3.jar` | 370,742 字节 | `BE7C596E0880633678F236F99C8D5C88FECF6E91391EC17AE5F97F57C4B7359A` |
| `HechaoEconomyScreen-NeoForge-1.21.1-0.1.3.jar` | 27,450 字节 | `68891BD42A91F12036DC18F147DB9576EFAAD5F6B695A8E501CFAAF827F638E3` |

## 验证与边界

- HechaoEconomy：`15/15`；
- HechaoEconomyScreen：`8/8`；
- 完整 .NET 解决方案：`797` 通过、`1` 条件测试跳过；
- 真实隔离 PostgreSQL：`1/1`；
- 当前没有实现第三方服务器官方身份签名。第三方服务器不能自定义官方界面文案，但仍可
  请求客户端内置 action；这不是服务器身份认证。

`0.1.2 -> 0.1.3` 的屏幕网络协议不兼容。客户端档案、服务端 NeoForge 模组和 Bukkit
插件必须按同一维护窗口部署。生产启动和真人交易验收仍需单独记录。

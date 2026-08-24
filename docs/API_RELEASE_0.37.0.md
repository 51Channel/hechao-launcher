# 赫朝启动器 API 0.37.0 发布记录

- 发布日期：`2026-08-24`
- 发布目录：`/opt/hechao-launcher-api/releases/0.37.0-20260823T182444Z`
- 源码提交：`9e75d5ef0dac5a189f9fd2891620042f7bd6c0ce`
- 正式标签：`api-v0.37.0`
- 配套经济插件：`HechaoEconomy 0.2.4`
- 启动器更新通道：`LatestVersion=0.15.10`、`MinimumSupportedVersion=0.12.3`

## 功能范围

本版把官方商城购买合同纳入 API，并继续保持服务器回收目录和玩家市场的业务边界：

- `/prices` 继续只提供服务器回收目录；
- `/shop` 提供按服务端标识隔离的官方购买目录；
- 购买在数据库事务内校验商品、锁定余额、写入销毁分录并创建待领取记录；
- 购买和领取使用幂等键，背包不足、断线和结果未知时不重复扣款或发放；
- 带命名空间或路径的物品 ID 使用查询参数管理路由，保留旧路径路由兼容；
- 商城售价必须高于同商品回收价，避免通过官方商城配置形成系统套利。

本版包含数据库迁移 `035_economy_server_shop`。API、经济插件和 Screen 必须按本记录的
配套版本使用，不能单独把旧插件连接到新商城合同。

## 制品与配置备份

| 制品/备份 | 大小 | SHA-256 |
| --- | ---: | --- |
| `Hechao.Api` | `105,704,004` 字节 | `F5C89C28ED6A6D2B585960AE36FA96250FAFD36CA9333D3D3B367F0BAC986450` |
| 更新环境备份 `environment.launcher-updates.20260824T053328Z.bak` | - | `DBBDD10DD672C163247ACFA685E3752CBD87C3555B80B4B3D1E72B71E14E66A7` |

环境备份权限为 `600`，属主为 `root:root`。备份和发布结果中不保存 AccessKey、令牌、
Cookie 或签名下载地址。

## 验证

- 完整 .NET 解决方案：`829` 通过、`1` 项隔离 PostgreSQL 测试按环境跳过、`0` 失败；
- API、商城迁移和服务端契约测试：`382` 通过、`0` 失败；
- `/healthz`、`/readyz` 和数据库就绪检查均正常；
- 当前 API 主进程 PID：`1838027`，`NRestarts=0`；
- 发布后 warning 及以上日志：`0`；
- 启动器更新通道返回 `0.15.10`，安装包大小与 SHA-256 与发布制品一致；
- 本轮未启动、停止或重启任何 Minecraft、Velocity 或服控代理进程。

## 回滚

API 程序故障时原子切回
`/opt/hechao-launcher-api/releases/0.36.1-20260821T083823Z`，恢复本记录的环境备份，
只重启 `hechao-launcher-api.service`。如果商城数据库合同需要整体回滚，先停止新版本插件，
按数据库恢复规程处理；不得删除购买审计、待领取记录、余额分录或不可变发布对象。

结构化证据见
[`evidence/API_0.37.0_PRODUCTION_DEPLOYMENT_2026-08-24.json`](evidence/API_0.37.0_PRODUCTION_DEPLOYMENT_2026-08-24.json)。

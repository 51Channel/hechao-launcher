# 启动器运行遥测与统计

> 启动器当前生产：`0.12.0`
>
> API 当前生产：`0.22.0-20260729T144953Z`（遥测行为自 `0.18.0` 起保持兼容）
>
> 当前状态：服务端遥测已生产部署；启动器 `0.12.0` 已完成私有 OSS 发布，首条真实启动事件仍来自 `0.11.14`

## 1. 目的与隐私边界

遥测只用于回答以下运维问题：

- 客户端安装、修复和启动的成功率与失败分类。
- 实际活跃的启动器版本和客户端档案版本。
- 安装与修复传输的字节量。
- 发布后是否出现集中失败，是否需要暂停或回滚。

客户端只上传固定枚举、版本号、UTC 时间、耗时和字节数。不会上传账号名、
邮箱、Minecraft UUID、IP 地址、文件路径、异常文本、日志、诊断包内容、服务器地址
或玩家输入。服务端会按已认证赫朝账号保存内部用户 ID，只用于去重和统计独立用户数，
管理页面不返回用户明细。

## 2. 事件模型

事件类型固定为：

```text
LauncherStarted
Install
Repair
Rollback
Launch
GameExit
```

结果固定为 `Success`、`Failure` 或 `Canceled`。失败原因使用
`LauncherTelemetryFailureCode` 中的固定分类，不允许上传任意错误文本。成功事件的
失败代码必须是 `None`，失败或取消事件必须带非 `None` 分类。

每条事件包含随机 `eventId`。API 使用 `(user_id, event_id)` 作为主键，因此网络重试
不会重复计数。单批最多 50 条；客户端时间只能在服务端当前时间前 30 天至后 5 分钟内。

## 3. 客户端队列

离线队列位于：

```text
%LocalAppData%\Hechao\Launcher\telemetry-outbox.json
```

- 最多保留 500 条和最近 30 天。
- 每批最多提交 50 条，成功后原子更新队列文件。
- 登录成功以及新事件写入后都会尝试刷新，失败时保留到下次机会。
- 队列读取、写入或网络上报失败不会阻断安装、修复、回滚、启动或退出流程。
- 遥测文件不进入诊断包，也不保存 Bearer、密码或 Microsoft 凭据。

## 4. API 与数据库

玩家端点：

```text
POST /v1/telemetry/events
Authorization: Bearer <launcher access token>
```

成功响应返回本批 `accepted` 与 `duplicates`。迁移
`015_launcher_telemetry.sql` 创建
`launcher.client_telemetry_events`；后台任务每 6 小时删除接收时间超过 30 天的记录。
迁移是加法变更，回滚到 `0.17.0` 时可保留表，但旧版本不会继续接收或清理新事件。

管理员端点：

```text
GET /v1/admin/telemetry/summary?hours=24
GET /v1/admin/telemetry/summary?hours=168
GET /v1/admin/telemetry/summary?hours=720
```

只接受 24 小时、7 天和 30 天三个窗口，并要求已完成 MFA 的管理员浏览器会话。
返回事件数、独立用户、安装/修复和启动结果、失败率、传输字节、启动器版本分布、
档案版本分布和失败分类。管理后台“运行数据”页面只展示这些聚合值。

## 5. 验收

源码回归：

```powershell
dotnet test Hechao.Launcher.sln --configuration Release --no-restore
node --check src/Hechao.Api/wwwroot/admin/admin.js
bash -n deploy/linux/smoke-test-admin-profile-releases.sh
git diff --check
```

`0.18.0` 候选已通过 `314/314` 个 .NET 测试。隔离生产副本验收使用真实生产数据库
备份、签名档案和候选单文件程序，验证了：

- 同一认证用户重复提交同一批次时只计数一次。
- 迁移 15、24 小时汇总、版本分布和失败分类正常。
- 既有签名导入、Test/Gray/Production、暂停、自动回滚和修订冲突没有回归。
- 测试数据库、临时 systemd 单元和临时目录在退出时全部清理。

生产切换已验证：

1. `/healthz`、`/readyz` 报告 `0.18.0` 和数据库 ready。
2. 迁移记录最大值为 15，API systemd `NRestarts=0`。
3. 未认证遥测与管理员汇总均返回 401，隔离认证批次重复提交不重复计数。
4. `hechao.world`、`api.hechao.world`、旧目录、下载和 Velocity 授权回归正常。

2026-07-27 已用真实安装的 `0.11.14` 产生一条
`LauncherStarted / Success / None`，发生时间为
`2026-07-27T13:51:06.674982Z`，客户端离线队列已清空。真实管理员 MFA 也已登记。
还需在“运行数据”切换 24 小时、7 天和 30 天三个窗口，并通过后续安装、修复、
启动和退出产生更完整样本；单条启动事件不能用于推导整体成功率。

## 6. 回滚

应用故障时按标准发布脚本把 `current` 切回
`0.17.0-20260726T231515Z`。迁移 15 不删除；这样重新部署 `0.18.0` 后仍可继续使用
已有统计。若必须停止收集，先回滚 API，再发布不创建遥测服务的旧启动器；不要手工删除
玩家本地队列或生产表。

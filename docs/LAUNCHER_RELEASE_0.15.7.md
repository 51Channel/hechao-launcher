# 赫朝启动器 0.15.7 发布记录

- 发布日期：2026-08-10
- 正式构建源码提交：`c0ddf9b2dfb65d64b8990242fa99addf5a008961`
- 正式标签：`launcher-v0.15.7`
- 生产通道切换时间：`2026-08-10T06:48:09Z`

## 变更内容

1. 进入 Launcher 活动页时立即刷新活动目录，停留期间每 30 秒同步一次，离开后停止
   轮询；后台刷新失败时保留最后一份安全快照。
2. 跨午夜企划严格按 `[开始, 结束)` 投影到月历，结束时刻恰为 `00:00` 时不再误占结束日。
3. 侧栏账户卡移除与“账户”工作区重复的操作按钮和预留空白，登录、退出和后台入口
   继续使用原有工作区。
4. Launcher 后台、官网后台、Launcher 活动页和官网公开日历继续读取 Launcher API
   `0.30.0` 的同一组 PostgreSQL 企划。官网只显示公开时间，活动客户端只能在 Launcher
   内下载。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.7-win-x64.exe` | `61,965,027` | `FA3542F3B9B7DFF9DE5CBC3605DC41882A834F7BD0E1AAEC432A210CDE673DB1` | `0.15.7` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,026,910` | `B136FAE0B3697ABA75B63C4D9B832FEC0B4EA35E011B530DFDC6638D576F264D` | `0.15.7+c0ddf9b2dfb65d64b8990242fa99addf5a008961` | `NotSigned` |

- 固定提交通过完整解决方案 `721/721`、Launcher `227/227`、API `302/302`、Launcher
  后台 Vitest `11/11`；Release 构建为 0 warning、0 error。
- 主页 `1590 x 960`、`1673 x 960`、`2250 x 1290` 与活动页 `1500 x 860`、
  `1060 x 640` 渲染检查无重叠、裁切或横向溢出。
- `0.15.6 -> 0.15.7` 覆盖安装、全新安装和两轮卸载均通过；设置、DPAPI 会话文件和
  验收开始时的既有启动器进程均保留。

## 私有 OSS 与更新通道

- 不可变对象：
  `releases/launcher/0.15.7/Hechao-Launcher-Setup-0.15.7-win-x64.exe`。
- Publisher CLI `1.3.0` 首次上传成功；第二次核对长度、版本、文件名和 SHA-256 后确认
  远端对象一致并跳过覆盖。
- 两轮独立签名下载均为 `200`，长度和 SHA-256 一致；匿名读取为 `403`。签名 URL 未
  进入终端、Git 或文档，远端暂存、结果文件和瞬态单元已清理。
- 生产通道为 `LatestVersion=0.15.7`、`MinimumSupportedVersion=0.12.3`；API 保持
  `0.30.0-20260809T232800Z`，只重启 `hechao-launcher-api.service`，`NRestarts=0`。
- 切换前环境备份为
  `/etc/hechao-launcher-api/environment.launcher-updates.20260810T064811Z.bak`；环境文件
  权限保持 `root:root 600`。本机和公网健康、就绪、公开元数据、公开下载、官网与
  Launcher 管理站均通过，切换后 warning/error 为 0。

## 四处日历与下载边界

- Launcher API 内部桥接返回 `0` 条当前企划、`2` 个可绑定整合包和单一活动槽；公开
  投影为 `0` 条活动，且不包含整合包、档案、清单、对象键或下载地址字段。
- Launcher 管理站 `/admin/activity-plans`、官网后台 `/admin/activity-plans`、官网公开
  `/community/calendar` 和 Launcher 活动页均部署同一合同。双后台分别按 5 秒和 10 秒
  刷新，Launcher 活动页按 30 秒刷新。
- 官网 `/download` 显示 `0.15.7`，只允许下载赫朝启动器；活动客户端下载能力没有进入
  官网页面或公开活动响应。

## 残余验证、运行边界与回滚

当前 Windows 管理机没有可恢复的 Launcher 登录会话，认证更新验收工具在读取元数据前
按预期停止；因此没有把认证端点的真实客户端回读记为通过。私有 OSS 双轮完整回读、公开
元数据和官网下载网关已经通过，认证链代码沿用 `0.15.6` 已验收实现。管理员下次完成
Launcher 登录后，应补跑一次 `0.15.6 -> 0.15.7` 认证更新计划和完整下载复验。

常驻 Publisher Agent 保持 `1.2.1` 和原 PID。本次没有修改 PostgreSQL、企划、整合包、
活动槽或服务器目录，也没有启动、停止或重启 Minecraft、Velocity、服控代理或游戏服。
若分发异常，恢复上述 API 环境备份并只重启 Launcher API，或禁用更新通道。已经安装
`0.15.7` 的客户端不会自动降级；修复必须发布更高版本。

结构化证据见
[`evidence/LAUNCHER_0.15.7_RELEASE_ACCEPTANCE_2026-08-10.json`](evidence/LAUNCHER_0.15.7_RELEASE_ACCEPTANCE_2026-08-10.json)。

# 赫朝启动器 0.15.4 发布记录

- 发布日期：2026-08-09
- 功能来源提交：`1f17d3fede2e1decf8e552411984b7b762e9db1c`、
  `163e7506b14060da1ed340145ac188da6f698e30`
- 正式构建源码提交：`163e7506b14060da1ed340145ac188da6f698e30`
- 正式标签：`launcher-v0.15.4`
- 生产通道切换时间：`2026-08-09T07:51:32Z`

## 变更内容

1. 主页按确认参考图统一为完整三栏结构：最左侧品牌导航、中间服务器目录和右侧主视区
   使用稳定宽度与共同基线。
2. 顶部栏移除重复面包屑与通知入口，只保留设置和窗口操作；导航改为紧凑节奏，服务器
   目录与当前服务器主卡片从同一高度开始。
3. 当前服务器横幅与详情以 `47 / 53` 比例并列在同一顶卡；公告与近期活动使用相同比例，
   快捷设置保持在其下方同一阅读路径内。
4. 玩家皮肤、名称、登录状态和访问身份收进左下账户面板；`51Channel` 等最长已验收名称
   可完整显示。
5. 服务器目录、客户端下载、自更新、账号、活动月历、快捷设置和游戏启动状态机保持
   不变；没有加入参考图中的虚构收藏、添加服务器或伪造运营数据。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.4-win-x64.exe` | `61,965,169` | `42D60ED9149D4ED03A9B0958482DEF9FE004C212CE95BA39912E366A665194B5` | `0.15.4` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,016,318` | `96C45B1F7BFDBB04CA360F45CDE0D84EC787FD3F4E271FA9F3397F0B314F4ECC` | `0.15.4+163e7506b14060da1ed340145ac188da6f698e30` | `NotSigned` |

- Release 完整解决方案测试：`710/710`，其中 Launcher `225/225`、Publisher `55/55`。
- Release 构建为 `0` 警告、`0` 错误；XAML XML、JSON、`git diff --check`、
  Impeccable detector、PowerShell 7 和发布溯源检查通过。
- 参考窗口 `1673 x 960`、宽屏 `2250 x 1290` 与最小窗口 `1590 x 960` 的真实 WPF
  截图确认顶部栏、导航、目录、主卡片、公告/活动和快捷设置基线一致，无重叠、裁切或
  横向溢出。
- 隔离安装验收：`0.15.3 -> 0.15.4`、全新安装和两轮卸载均通过；设置、DPAPI
  会话与既有启动器进程均保留。

## 私有 OSS

- 不可变对象：
  `releases/launcher/0.15.4/Hechao-Launcher-Setup-0.15.4-win-x64.exe`。
- Publisher CLI `1.3.0` 在阿里云一次性限权 systemd 单元中首次上传；第二次核对长度、
  元数据和 SHA-256 后跳过覆盖。
- 两轮独立签名下载均为 `200`，长度和 SHA-256 与正式安装包一致；匿名读取为 `403`。
- 签名 URL 未进入终端、Git 或文档；远端临时 Publisher、安装包、结果文件和脚本均已
  精确清理。

## 生产更新通道

- `LatestVersion=0.15.4`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61965169`
- `InstallerSha256=42d60ed9149d4ed03a9b0958482def9fe004c212ce95ba39912e366a665194b5`
- API 保持 `0.29.0` 与原发布目录，只重启 `hechao-launcher-api.service`；PID 从
  `418090` 变为 `530472`，`NRestarts=0`，切换后 warning/error 日志为 `0`。
- 环境文件保持 `root:root 600`；正式切换前备份为
  `/etc/hechao-launcher-api/environment.launcher-updates.20260809T075132Z.bak`，备份
  SHA-256 为 `ddb99ad14358352fb80dfd8cc87697923c76495782f92b7502d47f9277a70c00`。
- 新环境 SHA-256 为
  `901015494dcd5c93b0f8ef3054c5af942da08ef4b9a2523617fe9ae80a482cae`。
- 内外网健康/就绪、官网、后台、中转站、公开活动和公开下载入口均通过；公开活动为
  `0` 条，公开下载为 HTTPS `302`。

## 真实更新链验收

- 现有 DPAPI 会话恢复成功，生产 API 返回 `0.15.4`、最低版本 `0.12.3`、正确长度与
  SHA-256。
- `0.15.3` 生成更新计划，`0.15.4` 不生成重复更新计划。
- API 签发地址完整下载返回 `200`，共 `61,965,169` 字节，SHA-256 与发布制品一致。
- 验收工具未输出账号身份、会话令牌或签名 URL，没有关闭或替换正式启动器进程。

## 运行边界与回滚

常驻 Publisher Agent 保持 `1.2.1`、PID `2064` 和 `NRestarts=0`；本次没有启动、停止
或重启 Minecraft、Velocity、服控代理或任何游戏服务。若分发异常，恢复上述 API 环境
备份并只重启 Launcher API，或将 `LauncherUpdates__Enabled=false`。已经安装
`0.15.4` 的客户端不会自动降级；修复必须发布更高版本，不能覆盖本对象或标签。

结构化证据见
[`evidence/LAUNCHER_0.15.4_RELEASE_ACCEPTANCE_2026-08-09.json`](evidence/LAUNCHER_0.15.4_RELEASE_ACCEPTANCE_2026-08-09.json)。

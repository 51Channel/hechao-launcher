# 赫朝启动器 0.15.6 发布记录

- 发布日期：2026-08-10
- 正式构建源码提交：`181ce7cd6e73e1782b56c869873281645b599260`
- 正式标签：`launcher-v0.15.6`
- 生产通道切换时间：`2026-08-09T23:25:44Z`

## 变更内容

1. 当前服务器详情由整体垂直居中改为上下锚定：标题、说明、状态和分类贴近横幅顶部，
   主操作与三点菜单稳定对齐横幅底边。
2. 首页主操作由 `156 x 40` 收紧为 `148 x 40`，水平内边距改为 `18`；三点菜单保持
   `40 x 40` 的稳定点击区域，两项操作均不随窗口尺寸拉伸。
3. 只调整首页视觉布局；服务器目录、客户端安装与更新、登录、游戏启动、活动月历和
   客户端操作菜单的业务绑定与状态机均未修改。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.6-win-x64.exe` | `61,962,345` | `00C5C21CE8ABEA2FB15DA49DFB2CD5BA267E582DE49DBF96D91EF938C41822B3` | `0.15.6` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,017,381` | `8D7670AFA2FEA6B6E781F2A7BE9A43AFC33980B9298249A47475B98802DDD6C1` | `0.15.6+181ce7cd6e73e1782b56c869873281645b599260` | `NotSigned` |

- 固定提交通过完整解决方案 `710/710`、Launcher `225/225`、Publisher `55/55`；
  Release 构建为 `0` 警告、`0` 错误。
- `1590 x 960`、`1673 x 960`、`2250 x 1290` 和 `2349 x 1529` 四档真实 WPF
  截图均无重叠、裁切、横向溢出或操作漂移，Taste 与 Impeccable 审查无发现。
- `0.15.5 -> 0.15.6` 覆盖安装、全新安装和两轮卸载均通过；设置、DPAPI 会话和
  验收开始时的既有启动器进程均保留。

## 私有 OSS

- 不可变对象：
  `releases/launcher/0.15.6/Hechao-Launcher-Setup-0.15.6-win-x64.exe`。
- 第一个瞬态单元在进程启动前因 `PrivateTmp` 隐藏 `/var/tmp` 而以
  `226/NAMESPACE` 失败；结果文件为空、没有 OSS 请求或对象写入。将同一批已校验制品
  移到 Publisher 的受管 `/var/lib` 暂存区后，继续保留命名空间隔离并成功发布。
- Publisher CLI `1.3.0` 首次上传成功；第二次核对长度、版本、文件名和 SHA-256 后
  确认远端对象一致并跳过覆盖。
- 两轮独立签名下载均为 `200`，长度与 SHA-256 一致；匿名读取为 `403`。签名 URL
  未进入终端、Git 或文档。
- 远端暂存、结果文件、瞬态单元和瞬态凭据挂载均已清理；常驻 Publisher 的一个凭据
  目录保持原基线不变。

## 生产更新通道

- `LatestVersion=0.15.6`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61962345`
- `InstallerSha256=00c5c21ce8abea2fb15da49dfb2cd5ba267e582de49dbf96d91ef938c41822b3`
- API 保持 `0.29.0` 与原发布目录，只重启 `hechao-launcher-api.service`；PID 从
  `850248` 变为 `1013686`，`NRestarts=0`。
- 切换前环境备份为
  `/etc/hechao-launcher-api/environment.launcher-updates.20260809T232544Z.bak`，SHA-256
  为 `95EC859DE8454B5BAADCB80533F5B1C7707BF679BD164723D0F5D40EAC2B5027`；新环境
  SHA-256 为 `ADA62C60E6AC1B5EE1501371F5715B2EFDE3FC5FDC56792A0B95CAE283A704AE`，
  权限保持 `root:root 600`。
- 本机与公网健康/就绪、公开元数据、公开下载入口、官网、后台、中转站和公开活动均
  通过；切换时公开活动为 `2` 条，切换后 warning/error 日志为 `0`。

## 真实更新链验收

- 现有 DPAPI 会话恢复成功，生产 API 返回 `0.15.6`、最低版本 `0.12.3`、正确长度与
  SHA-256。
- `0.15.5` 生成更新计划，`0.15.6` 不生成重复更新计划。
- API 签发入口完整下载返回 `200`，共 `61,962,345` 字节，SHA-256 与正式制品一致。
- 验收工具未输出账号身份、会话令牌或签名 URL。
- 最终本机正式安装目录仍为 `0.15.5`；切换前已启动的 PID `5980` 保持运行，本次
  发布没有强制关闭或替换它。它和其他旧客户端会在下次启动并恢复登录态后自动更新。

## 运行边界与回滚

常驻 Publisher Agent 保持 `1.2.1`、PID `2064` 和 `NRestarts=0`；本次没有启动、停止
或重启 Minecraft、Velocity、服控代理或任何游戏服务。若分发异常，恢复上述 API 环境
备份并只重启 Launcher API，或将 `LauncherUpdates__Enabled=false`。已经安装
`0.15.6` 的客户端不会自动降级；修复必须发布更高版本，不能覆盖本对象或标签。

结构化证据见
[`evidence/LAUNCHER_0.15.6_RELEASE_ACCEPTANCE_2026-08-10.json`](evidence/LAUNCHER_0.15.6_RELEASE_ACCEPTANCE_2026-08-10.json)。

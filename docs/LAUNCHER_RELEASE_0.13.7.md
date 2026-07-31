# 赫朝启动器 0.13.7 发布记录

> 状态：已发布到私有 OSS，生产更新通道已启用
>
> 正式标签：`launcher-v0.13.7`
>
> 制品源码提交：`b6fa703d03359519ae5d73336dc974409cf80937`

## 1. 变更

- 自更新弹窗改为固定行布局，标题、更新说明、进度、状态和操作按钮不再互相挤压。
- 更新说明使用限高滚动区域，长文本不会继续撑高弹窗。
- 进度条和下载状态分行显示，主按钮文字与图标显式使用白色。
- 新增 XAML 布局契约测试，防止后续改动重新引入自适应行错位。
- 配套服控生产修复已上线：离线目标端口不再被代理误判为探测失败，生产 API 已启用服控。

## 2. 制品

| 制品 | 字节 | SHA-256 | ProductVersion | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.13.7-win-x64.exe` | `61,910,615` | `5E7DD73BED96BE98EEB616CCF8155D268B92C8A0A25B54BB4D67F0712429B5CF` | `0.13.7` | `NotSigned` |
| `Hechao.Launcher.exe` | `68,856,376` | `6C98FF66450E9FF8F91A00945BB7F7F8BF12200E071E82D34F978B751C21860A` | `0.13.7+b6fa703d03359519ae5d73336dc974409cf80937` | `NotSigned` |

私有对象：
`releases/launcher/0.13.7/Hechao-Launcher-Setup-0.13.7-win-x64.exe`。
对象不可覆盖，文档和日志不保存短时签名 URL。

## 3. 验收

- 完整解决方案 `458/458`，其中启动器测试 `135/135`、服控代理测试 `14/14`。
- 完成 `0.13.6 -> 0.13.7` 覆盖安装、全新安装和两轮卸载。
- 设置、登录会话和既有启动器进程均保留。
- 125% DPI 的 `1875 x 1075` 视觉预览中，说明、进度、状态和按钮无重叠。
- 私有 OSS 首次上传成功；第二轮发布校验后跳过。两轮匿名读取均为 `403`，签名读取均为 `200`，长度与 SHA-256 一致。
- 生产 API 为 `0.23.1`，`hechao-launcher-api.service` 为 `active`、`NRestarts=0`，公网健康端点返回 `200`。
- 生产服控复核为 9 个目标、2 个在线代理、4 个运行中实例，操作和命令队列均为 0；部署前后的四个游戏 PID 未变化。

当前正式机仍运行 `0.13.6`。为避免打断正在运行的游戏，没有强制聚焦启动器并点击
“下载并重启”；真实 `0.13.6 -> 0.13.7` 自更新点击验收保持为待办，不影响安装包、
OSS、API 更新通道和布局测试结论。

结构化证据见
[`evidence/LAUNCHER_0.13.7_RELEASE_ACCEPTANCE_2026-07-31.json`](evidence/LAUNCHER_0.13.7_RELEASE_ACCEPTANCE_2026-07-31.json)
和
[`evidence/SERVER_CONTROL_PRODUCTION_DEPLOYMENT_2026-07-31.json`](evidence/SERVER_CONTROL_PRODUCTION_DEPLOYMENT_2026-07-31.json)。

## 4. 生产状态

- `LatestVersion=0.13.7`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61910615`
- `InstallerSha256=5e7dd73bed96be98eeb616ccf8155d268b92c8a0a25b54bb4d67f0712429b5cf`
- 发布时间：`2026-07-31T04:11:26Z`

## 5. 回滚

分发故障时先设置 `LauncherUpdates__Enabled=false` 并只重启 API。已经安装新版本的
玩家不会被自动降级；需要代码回滚时，从上一稳定源码构建并发布更高版本号。不得
覆盖 `0.13.7` 的 OSS 对象或移动正式标签。

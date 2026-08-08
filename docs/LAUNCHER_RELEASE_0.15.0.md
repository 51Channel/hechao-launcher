# 赫朝启动器 0.15.0 发布记录

- 发布日期：2026-08-08
- 功能来源提交：`b1f049563774c8a941afbe283bc072c011f4fb8e`
- 发布准备提交：`cb24a4a4efa83d76155b1a16757b25879349ca19`
- 正式标签：`launcher-v0.15.0`
- 生产通道切换时间：`2026-08-08T08:44:04Z`

## 变更内容

1. “活动”页改为与官网同源的活动月历，二者都读取 Launcher API 的活动排期。
2. 月历固定显示六周并以周一开头，支持前后月份、回到今天、选择日期、跨日活动、
   待排期活动和所选日期详情。
3. 当前没有活动时仍显示完整月历，只展示同步状态，不回退为整页空状态。
4. 活动客户端的下载、更新和下载后加入服务器主页闭环保持不变。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.0-win-x64.exe` | `61,960,670` | `35B2A08450E125ACC6EF096DEC4B3D768BF405CFAF2A7C20CF12FD1F52A38A96` | `0.15.0` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,013,253` | `7B1282C8FA4A5BE8B5B038AB621388CBF3C67206E04B69FD96B9CF1BD0F09793` | `0.15.0+cb24a4a4efa83d76155b1a16757b25879349ca19` | `NotSigned` |

- Release 完整解决方案测试：`704/704`，其中 Launcher `219/219`、Publisher `55/55`。
- 默认窗口 `1440 x 900` 与窄窗口 `1180 x 720` 离屏截图通过，无重叠或横向溢出。
- 隔离安装验收：`0.14.2 -> 0.15.0`、全新安装和两轮卸载均通过；设置、会话与
  既有正式启动器进程均保留。

## 私有 OSS

- 不可变对象：
  `releases/launcher/0.15.0/Hechao-Launcher-Setup-0.15.0-win-x64.exe`。
- 首次发布上传成功；第二次发布核对长度、元数据和 SHA-256 后跳过覆盖。
- 两轮独立签名下载均返回 `200`，长度和 SHA-256 与正式安装包一致。
- 两轮匿名读取均返回 `403`；签名 URL 未进入终端记录、文档、证据或 Git。

## 生产更新通道

- `LatestVersion=0.15.0`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61960670`
- `InstallerSha256=35b2a08450e125acc6ef096dec4b3d768bf405cfaf2a7c20cf12fd1f52a38a96`
- API 保持 `0.29.0` 与原发布目录，只重启 `hechao-launcher-api.service`。新 PID
  `3023712`，`NRestarts=0`，切换后错误级日志为 `0`。
- 环境文件仍为 `root:root 600`；切换前备份为
  `/etc/hechao-launcher-api/environment.launcher-updates.20260808T084404Z.bak`。
- 本机与公网健康/就绪、官网、后台、中转站和公开活动源均返回 `200`，数据库为
  `ready`。公开活动当前为零条，官网与启动器仍显示完整月历。

## 真实更新链验收

- 使用现有 DPAPI 会话恢复真实登录态，生产 API 返回 `0.15.0`、最低版本
  `0.12.3`、正确长度、SHA-256 和 HTTPS 私有下载地址。
- `0.14.2` 生成更新计划，`0.15.0` 不生成重复更新计划。
- API 签发地址完整下载返回 `200`，共 `61,960,670` 字节，SHA-256 与发布制品一致。
- 验收工具不输出账号身份、会话令牌或签名 URL；正式安装进程未被代替关闭或重启。

## 运行边界与回滚

常驻 Publisher Agent 保持 `1.2.1`、原 PID 和 `NRestarts=0`；本次没有启动、停止或
重启 Minecraft、Velocity、服控代理或任何游戏服务。若分发异常，恢复上述 API 环境
备份并只重启 Launcher API，或将 `LauncherUpdates__Enabled=false`。已经安装
`0.15.0` 的客户端不会自动降级。

结构化证据见
[`evidence/LAUNCHER_0.15.0_RELEASE_ACCEPTANCE_2026-08-08.json`](evidence/LAUNCHER_0.15.0_RELEASE_ACCEPTANCE_2026-08-08.json)。

# 赫朝启动器 0.14.2 发布记录

- 发布日期：2026-08-01
- 构建源码：`c66f4d357089a77f392a27a1a411f66e5e28f1e4`
- 正式标签：`launcher-v0.14.2`
- 生产通道切换时间：`2026-08-01T09:16:27Z`

## 变更内容

1. 服务器目录结果改为按单次请求携带实时、缓存或内置来源；活动服进入前的权威刷新被取消、过期或回退时一律故障关闭。
2. 强制更新同时门禁主按钮命令与实际业务方法，避免旧启动器绕过最低版本限制。
3. 注册成功但自动登录失败时明确提示账号已经创建并引导重新登录；未预期账号响应不再从 WPF 点击事件逸出。
4. 运行中游戏状态和跨档案玩家设置增加有界跨进程文件锁、原子替换和异常退出恢复。
5. 账户页与左下角账号入口统一显示 Minecraft 皮肤头像，并保留无皮肤时的图标回退。
6. 前端审查修复与业务闭环详见 [`LAUNCHER_FRONTEND_AUDIT_2026-08-01.md`](LAUNCHER_FRONTEND_AUDIT_2026-08-01.md)。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.14.2-win-x64.exe` | `61,929,723` | `D71A6BAED73FE1A9F503DDC7282A73B8F2E4C51145B6A8562133058DF401D6D8` | `0.14.2` | `NotSigned` |
| `Hechao.Launcher.exe` | `68,934,230` | `9A34721D8A5A703DE43746E6840FC225E0B2057F9F7A3235ED0514D85EAFCBB7` | `0.14.2+c66f4d357089a77f392a27a1a411f66e5e28f1e4` | `NotSigned` |

- Release 全解决方案构建：`0` 警告、`0` 错误。
- 全解决方案测试：`564/564`；其中 Launcher `202/202`、API `233/233`、Distribution `45/45`、Publisher `32/32`、StatusCollector `16/16`、Backup `12/12`、ServerControlAgent `24/24`。
- 隔离安装验收：`0.14.1 -> 0.14.2` 升级、全新安装和两轮卸载均通过；设置、会话与既有启动器进程均保留。

## 私有 OSS

- 不可变对象：`releases/launcher/0.14.2/Hechao-Launcher-Setup-0.14.2-win-x64.exe`。
- 首次发布上传成功；第二次发布确认对象长度、元数据和 SHA-256 一致后跳过覆盖。
- 两轮签名下载均返回 `200`，长度和 SHA-256 与本地安装包一致。
- 两轮匿名访问均返回 `403`；发布记录未保存签名 URL。

## 生产更新通道

- `LatestVersion=0.14.2`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61929723`
- `InstallerSha256=d71a6baed73fe1a9f503ddc7282a73b8f2e4c51145b6a8562133058df401d6d8`
- 发行说明为 ASCII 单行，避免远程环境文件编码损坏。
- API `0.24.2` 保持原发布目录；仅重启 `hechao-launcher-api.service`。新 PID `853227`，`NRestarts=0`。
- 环境文件仍为 `root:root 600`，切换前备份为
  `/etc/hechao-launcher-api/environment.launcher-updates.20260801T091627Z.bak`。
- 本机与公网 `/healthz`、`/readyz` 均为 `200`，数据库为 `ready`；切换后错误级日志为 `0`。
- `/v1/catalog`、`hechao.world`、`api.hechao.world` 与管理后台回归均通过。

## 真实更新链验收

- 使用现有 DPAPI 会话走正式 `LauncherApiClient`，生产 API 返回 `0.14.2`、正确长度、SHA-256、发布时间和 HTTPS 私有下载地址。
- `0.14.1` 会生成可用更新计划；`0.14.2` 自身不生成重复更新计划。
- API 实际签发的地址完整下载 `61,929,723` 字节，HTTP `200`，SHA-256 与发布安装包一致。
- 本轮没有替用户关闭或重启正式启动器进程；已安装客户端将在下次正常启动检查时自行更新。

## 回滚

若分发异常，先将 `LauncherUpdates__Enabled=false`，或使用上述环境备份恢复完整
`0.14.1` 元数据后重启 `hechao-launcher-api.service`。已经安装 `0.14.2` 的客户端
不会自动降级；若代码需要回退，应从 `launcher-v0.14.1` 源码发布更高版本号。

结构化证据见
[`evidence/LAUNCHER_0.14.2_RELEASE_ACCEPTANCE_2026-08-01.json`](evidence/LAUNCHER_0.14.2_RELEASE_ACCEPTANCE_2026-08-01.json)。

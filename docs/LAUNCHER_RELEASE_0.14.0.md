# 赫朝启动器 0.14.0 正式发布

- 正式版本：`0.14.0`
- 源码提交：`bc04fea8d525663b3ae24f4a6dcfc6d1b219c986`
- 正式标签：`launcher-v0.14.0`
- 生产通道切换时间：`2026-07-31T14:26:00Z`

## 本次功能

1. 启动器在每个进程启动并恢复赫朝登录态后检查一次自身版本。发现新版时自动下载、校验、启动 updater 并关闭旧进程；失败时保留当前版本和手动重试入口。
2. 服务器详情增加“删除客户端”。删除受档案锁保护，只清理当前档案、上一版本副本和残留 staging；共享对象缓存、其他档案和玩家设置不会被删除。
3. 玩家通用 `options.txt` 设置写入 `shared/player-settings/options.txt`，在启动游戏前合并、游戏退出后回收。灵敏度和已有按键项跨档案共享，资源包、上次服务器和版本等档案私有键保持隔离。
4. 配套 API `0.24.2` 的管理后台会发现心跳正常、正在运行且尚未进入目录的服控目标，在新增服务器时辅助填表，不自动保存或启停服务器。

## 正式制品

| 制品 | 字节 | SHA-256 | ProductVersion | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.14.0-win-x64.exe` | `61,910,638` | `2F6ED3DBD94472DE99578DDCFA4CAFD504436575946722A6074A8F8C80AB0E8B` | `0.14.0` | `NotSigned` |
| `Hechao.Launcher.exe` | `68,879,271` | `81AD01021A50B79C319CC670AA058E82480453E38BF416249037A6CD5155ACDB` | `0.14.0+bc04fea8d525663b3ae24f4a6dcfc6d1b219c986` | `NotSigned` |

## 验收

- 完整解决方案测试：`492/492`。
- 分项：Distribution `45/45`、API `228/228`、Launcher `143/143`、Publisher `32/32`、StatusCollector `12/12`、Backup `12/12`、ServerControlAgent `20/20`。
- `node --check`、`dotnet format --verify-no-changes`、`git diff --check` 和 PowerShell 7 合规检查通过。
- `0.13.7 -> 0.14.0` 隔离覆盖升级、全新安装、两轮卸载、设置与会话保留、运行中启动器保护全部通过。
- 管理机完成 `0.13.6 -> 0.14.0` 最后一次引导升级；正式设置、会话、目录缓存、下载历史、退出记录和遥测队列哈希均保持不变，启动后账号恢复且界面正常。
- 真实数据目录已生成共享设置主副本，共 `277` 个键，其中按键项 `152` 个；存在 `mouseSensitivity`，档案私有排除键为 `0` 个。

`0.13.6` 本身只会提示更新，因此本次使用已验证的本地正式安装包完成最后一次引导升级。从 `0.14.0` 开始，后续版本会在启动时自动下载并重启，不再要求玩家重新下载安装包。

## 私有分发与生产通道

- 私有对象：`releases/launcher/0.14.0/Hechao-Launcher-Setup-0.14.0-win-x64.exe`。
- 首次发布完成上传；第二轮重复发布在远端校验一致后跳过。
- 两轮匿名读取均为 `403`，两轮签名读取均为 `200`，长度和 SHA-256 一致。
- 私有签名 URL 只保存在受保护的本机结果文件中，不进入仓库、文档或日志。
- 生产通道：`LatestVersion=0.14.0`、`MinimumSupportedVersion=0.12.3`。
- 通道切换后 API `0.24.2` 的本机与公网 `/healthz`、`/readyz` 均为 `200`，`NRestarts=0`。

## 回滚

分发异常时先设置 `LauncherUpdates__Enabled=false` 并只重启 `hechao-launcher-api.service`。已安装 `0.14.0` 的客户端不自动降级；程序修复使用更高版本号发布。不可变的 `0.13.7` 对象和标签继续保留作为安装级回滚依据。

结构化证据见 [`evidence/LAUNCHER_0.14.0_RELEASE_ACCEPTANCE_2026-07-31.json`](evidence/LAUNCHER_0.14.0_RELEASE_ACCEPTANCE_2026-07-31.json)。

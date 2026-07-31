# 赫朝启动器 0.13.6 发布记录

> 状态：已发布到私有 OSS，生产更新通道已启用，真实自更新验收通过
>
> 正式标签：`launcher-v0.13.6`
>
> 制品源码提交：`667a15a9eb48cfb2264c3d2f085abc7cbbe1c070`
>
> 上一内部验收构建：`0.13.5`，不创建标签，也不作为正式回滚目标

## 1. 发布内容

- 设置页和快捷设置新增“使用 Windows 系统代理”开关。
- 默认关闭时，启动器管理的 HTTP 请求全部直连；开启时继承 Windows 系统代理。
- 开关写入 `%LocalAppData%\Hechao\Launcher\settings.json`，下次启动生效。
- 代理策略覆盖赫朝 API、客户端与更新下载、论坛注册、Xbox/Minecraft 验证、
  皮肤请求和游戏资源请求。
- 保留 `0.13.4` 引入的更新下载失败分阶段诊断、OSS 安全错误码提取和日志脱敏。

## 2. 制品

| 制品 | 字节 | SHA-256 | 版本 | Authenticode |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.13.6-win-x64.exe` | `61,904,234` | `9A5F09BD5084C4C926598184A536825B4F59A0116571D255D4603F7CD54A4C03` | `0.13.6` | `NotSigned` |
| `Hechao.Launcher.exe` | `68,855,341` | `2365C71C4F8348C9FD13ECABF7BCB2A9B4117D06D472A2C4093A88AA654DFAA2` | `0.13.6+667a15a9eb48cfb2264c3d2f085abc7cbbe1c070` | `NotSigned` |

生产对象为
`releases/launcher/0.13.6/Hechao-Launcher-Setup-0.13.6-win-x64.exe`。
对象保持私有且不可变，不在仓库或文档中保存签名下载 URL。

## 3. 验收

- 完整解决方案 `456/456`，其中启动器测试 `134/134`。
- 隔离安装完成 `0.13.5 -> 0.13.6` 覆盖升级、全新安装和两轮卸载；
  设置、会话和既有启动器进程保护均通过。
- OSS 两轮发布校验均确认同键同内容；匿名读取 `403`，签名读取 `200`，
  长度和 SHA-256 与本机制品一致。
- 正式客户端通过自身“下载并重启”从 `0.13.5` 升级到 `0.13.6`。
  设置和 MSAL 缓存哈希不变；赫朝账号、Minecraft 皮肤、Java 路径、客户端目录
  与内存设置均保留。`session.dat` 仅因刷新令牌轮换发生预期变化。
- 正式机曾残留 `DisplayVersion=0.13.5`，但 EXE 与卸载器均已是 `0.13.6`。
  同一已校验安装包再次静默覆盖后，卸载项正确写为 `0.13.6`，设置和会话哈希
  均保持不变，证明无需重新构建或重新上传制品。
- 正式设置当前为 `UseSystemProxy=false`，更新错误日志不存在。

结构化证据见
[`evidence/LAUNCHER_0.13.6_RELEASE_ACCEPTANCE_2026-07-31.json`](evidence/LAUNCHER_0.13.6_RELEASE_ACCEPTANCE_2026-07-31.json)。

## 4. 生产状态

- API 更新通道：`LatestVersion=0.13.6`，`MinimumSupportedVersion=0.12.3`。
- `hechao-launcher-api.service` 为 `active`，`NRestarts=0`。
- 公网 `healthz` 与 `readyz` 均为 `200`。
- 环境文件回滚副本权限为 `600`；部署临时脚本已删除。

## 5. 回滚

先将 `LauncherUpdates__Enabled=false` 并重启 API，阻止继续分发。已经升级的客户端
不会自动降级。若需要程序级回滚，应从 `0.13.4` 的源码提交
`029160bdc05d4c8e78ad4bae594efd09f7de7bef` 修复后发布更高版本号；不得把无正式
源码标签的 `0.13.5` 暴露为回滚版本，也不得覆盖任何既有 OSS 对象。

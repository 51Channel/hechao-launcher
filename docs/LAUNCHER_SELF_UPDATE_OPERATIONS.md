# 启动器自更新

## 范围

启动器登录赫朝账号后查询 `GET /v1/launcher/update`。API 只返回当前正式安装包的
版本、最低支持版本、长度、SHA-256、发行说明和短时私有 OSS 地址。客户端仅接受
HTTPS（本机测试可用 loopback HTTP），下载支持断点续传，并在启动安装程序前重新
核对长度与 SHA-256。

更新器是当前单文件启动器的临时副本。它等待主程序退出，以 NSIS 静默覆盖安装，
再从当前用户注册表读取安装目录并启动新版本。下载、校验、启动更新器或安装失败时，
已安装版本不会被提前删除；错误写入
`%LocalAppData%\Hechao\Launcher\updates\last-update-error.log`，并尽力重新启动
原安装版本。

首版不依赖 Authenticode。完整性边界是 HTTPS、已认证 API、私有 OSS 签名地址与
API 提供的 SHA-256；因此 API 的发布凭据和管理员会话仍属于高敏感资产。

## 发布顺序

1. 完整构建和测试新启动器。
2. 使用 `tools/Publish-PrivateLauncherRelease.ps1` 将安装包写入不可变对象：
   `releases/launcher/<version>/Hechao-Launcher-Setup-<version>-win-x64.exe`。
3. 用两次独立签名回读核对长度与 SHA-256。
4. 确认 API RAM 策略包含
   `oss:GetObject` 的 `hechaoworld/releases/launcher/*`，不能授予写权限。
5. 在 API 主机运行 `deploy/linux/configure-launcher-updates.sh`，写入：

```text
LauncherUpdates__Enabled=true
LauncherUpdates__LatestVersion=0.13.0
LauncherUpdates__MinimumSupportedVersion=0.12.3
LauncherUpdates__InstallerBytes=<bytes>
LauncherUpdates__InstallerSha256=<sha256>
LauncherUpdates__PublishedAt=<UTC ISO-8601>
LauncherUpdates__ReleaseNotes=<single-line notes>
```

## 首次启用边界

`launcher-v0.13.0` 是第一份包含自更新模块的启动器。仍在运行
`launcher-v0.12.3` 或更早版本的玩家必须手动覆盖安装一次 `0.13.0`；
旧程序本身没有 `/v1/launcher/update` 客户端，无法自行完成这次跨越。

`0.13.0` 和 `0.13.1` 因更新弹窗的只读进度属性绑定错误，在真实启动验收时
被撤回且未向玩家开放。修复后的首次过渡版是 `0.13.2`。

从 `0.13.2` 开始，后续版本通过启动器内的更新提示下载、校验、静默覆盖并
重新启动。发布首轮真实验收使用 `0.13.2 -> 0.13.3`，不得把
`0.12.3 -> 0.13.2` 记作自更新验收。

6. 重启 API，先用测试账号核对可选更新，再提高
   `MinimumSupportedVersion` 开启强制更新。

配置脚本会先保留带 UTC 时间戳的环境文件备份。发布失败时不要改
`LatestVersion`；API 或 OSS 故障时，启动器继续保留并运行当前版本。

## 回滚

回滚只需将 `LatestVersion` 和对应长度、SHA-256 改回上一份仍保留的不可变安装包，
或将 `LauncherUpdates__Enabled=false` 后重启 API。已经安装新版本的玩家不会被
自动降级；需要降级时发布更高版本号且内容基于上一稳定版本，避免版本比较倒退。

## 验收

- 当前版本请求返回 `204` 或无更新。
- 旧版本收到可选或强制更新提示，下载进度不阻塞 UI。
- 断网、截断安装包和错误 SHA-256 都不关闭当前版本。
- 安装中失败会留下错误日志并重新打开原版本。
- 成功更新后，设置、DPAPI 会话、客户端档案和游戏目录保持不变。
- 匿名 OSS 请求返回 `403`，API RAM 只有读取两个批准前缀的权限。

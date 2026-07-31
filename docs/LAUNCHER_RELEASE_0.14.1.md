# 赫朝启动器 0.14.1 正式发布

- 正式版本：`0.14.1`
- 源码提交：`0abb8e93873a371b6901496ee8b7484946321634`
- 正式标签：`launcher-v0.14.1`
- 生产通道切换时间：`2026-07-31T18:25:01Z`

## 变更范围

1. 将检查、修复、回滚和删除客户端四个维护按钮从右侧“运行配置”移动到左侧
   “客户端准备”，让客户端操作与 Java、内存等运行参数分区。
2. 增加 XAML 结构契约测试，固定维护按钮所属面板、行号和按钮数量，防止后续布局回退。
3. owl9 的恐怖整蛊与真正 PVP 识别修复属于 StatusCollector `0.2.2`，不混入本启动器
   二进制；对应证据见 [`STATUS_COLLECTOR_RELEASE_0.2.2.md`](STATUS_COLLECTOR_RELEASE_0.2.2.md)。

## 制品

| 制品 | 字节 | SHA-256 | ProductVersion | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.14.1-win-x64.exe` | `61,912,187` | `6B31535E3A5CF08FF6D5F3D2B9CAD1B69DD5B3E3242959132DFDAD240476413E` | `0.14.1` | `NotSigned` |
| `Hechao.Launcher.exe` | `68,879,293` | `E47EC9EC634FB92307A4D1246B2A421EDDB659DCD5C78EE48E1028462C574449` | `0.14.1+0abb8e93873a371b6901496ee8b7484946321634` | `NotSigned` |

## 自动化与安装验收

- 完整解决方案测试：`501/501`。
- 分项：Distribution `45/45`、API `228/228`、Launcher `144/144`、Publisher
  `32/32`、StatusCollector `16/16`、Backup `12/12`、ServerControlAgent `24/24`。
- 已完成 `0.14.0 -> 0.14.1` 隔离覆盖安装、全新安装和两轮卸载。
- 覆盖安装前后的设置与 DPAPI 会话保持不变，既有正式启动器进程未被关闭或替换。
- 安装包、安装后 EXE 的长度和 SHA-256 与发布记录一致。

## 私有 OSS

- 不可变对象：
  `releases/launcher/0.14.1/Hechao-Launcher-Setup-0.14.1-win-x64.exe`。
- 第二次发布在读取并校验既有对象后跳过覆盖。
- 匿名读取为 `403`；两轮签名读取均为 `200`，长度和 SHA-256 一致。
- AccessKey、会话令牌和短时签名 URL 均未进入仓库或发布记录。

## 生产更新通道

- `LatestVersion=0.14.1`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61912187`
- `InstallerSha256=6b31535e3a5cf08ff6d5f3d2b9cad1b69dd5b3e3242959132dfdad240476413e`
- API `0.24.2` 保持 `active/running`，PID `292148`，`NRestarts=0`。
- 本机和公网 `/healthz`、`/readyz` 均为 `200`，数据库为 `ready`；成功切换后的
  error 级日志为空。

第一次切换通过远程 stdin 传递中文发行说明时发生 UTF-8 破坏，systemd 拒绝加载无效的
环境文件。部署流程立即恢复 `0.14.0` 配置并恢复 API，没有留下半切换状态。第二次改用
ASCII 单行发行说明后，配置原子切换和健康检查成功。该失败、回滚和重试均保留在结构化
证据中。

## 真实自动更新边界

生产更新通道已经开放，但管理机上的正式启动器仍保持原进程：PID `14928`、版本
`0.14.0+bc04fea8d525663b3ae24f4a6dcfc6d1b219c986`、SHA-256
`81AD01021A50B79C319CC670AA058E82480453E38BF416249037A6CD5155ACDB`。本次发布没有替
用户关闭、重启或覆盖该进程。

最终真人验收由用户亲自关闭并重新打开启动器，确认启动检查自动完成
`0.14.0 -> 0.14.1`，随后复核设置、会话和客户端档案。完成前不得把该项记录为已通过。

## 回滚

分发异常时先设置 `LauncherUpdates__Enabled=false`，或将完整版本、长度和 SHA-256 一并
恢复到不可变的 `0.14.0` 对象，然后只重启 `hechao-launcher-api.service`。已安装
`0.14.1` 的客户端不自动降级；程序修复使用更高版本号发布。

结构化证据见
[`evidence/LAUNCHER_0.14.1_RELEASE_ACCEPTANCE_2026-08-01.json`](evidence/LAUNCHER_0.14.1_RELEASE_ACCEPTANCE_2026-08-01.json)。

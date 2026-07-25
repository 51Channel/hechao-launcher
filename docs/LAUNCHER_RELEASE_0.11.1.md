# 赫朝启动器 0.11.1 发布记录

> 构建日期：`2026-07-25`
>
> 源码提交：`eb59c56fdc24cbaca710ae8f56768286cafe3c23`
>
> 状态：本机内部候选已验证，尚未上传 OSS 或面向玩家发布

## 修复内容

- 错误账号或密码返回 401 时显示可恢复的表单错误，不再作为未处理异常退出启动器。
- Microsoft 正版绑定继续使用系统浏览器、授权码和 PKCE，新增中文等待层、取消按钮，以及赫朝样式的中文成功和失败回调页。
- 主操作、客户端修复和 Microsoft 绑定改用带重复执行保护与异常边界的异步命令。
- 下载任务的只读进度绑定显式设为 `OneWay`，修复点击“安装客户端”时的 WPF 绑定崩溃。
- 清单校验、文件保留、哈希检查和原子目录切换移出界面线程，减少大型客户端安装期间的窗口假死。
- 安装准备、下载或目录切换出现未预期异常时，任务会安全结束并恢复界面，原活动客户端版本不会被替换。

## 根因记录

Windows 应用程序日志确认了两个独立的 .NET 未处理异常：

1. 登录失败时，API 客户端把登录端点的 401 映射成 `LauncherAuthenticationRequiredException`，该异常没有被登录表单接住。
2. 创建下载任务时，WPF 尝试把 `ProgressBar.Value` 以默认双向模式写回只读的 `DownloadJobViewModel.Percent`，触发 `InvalidOperationException`。异常随后穿过 `async void` 命令入口并终止进程。

本版本同时修复精确根因和命令入口异常边界，避免同类故障再次导致整个进程退出。

## 自动验证

| 项目 | 结果 |
| --- | --- |
| Debug 完整解决方案测试 | `181/181` 通过 |
| Release 完整解决方案测试 | `181/181` 通过 |
| 正式构建脚本内测试 | `181/181` 通过 |
| 编译警告 | `0` |
| `git diff --check` | 通过 |

新增回归覆盖：

- 错误密码 401 映射为带中文详情的 `LauncherApiException`。
- 异步命令捕获异常、恢复可执行状态并拒绝运行中重复点击。
- Microsoft 回环完成页包含中文成功、重试和安全提示。
- 活动下载进度 XAML 绑定保持显式 `Mode=OneWay`。

## 候选制品

| 项目 | 值 |
| --- | --- |
| 启动器 EXE | `artifacts/publish/win-x64/Hechao.Launcher.exe` |
| EXE 大小 | `68,628,296` 字节 |
| EXE SHA-256 | `0D556FA9EA616A0BBBD19292268A83904680C3B2ED69E041C60C399F81853404` |
| EXE FileVersion | `0.11.1.0` |
| EXE ProductVersion | `0.11.1+eb59c56fdc24cbaca710ae8f56768286cafe3c23` |
| 安装包 | `artifacts/installer/Hechao-Launcher-Setup-0.11.1-win-x64.exe` |
| 安装包大小 | `61,798,291` 字节 |
| 安装包 SHA-256 | `85D04FAB9731E59076733DCDACD45E84842624A418CF8E1C2951F16B7DB25BBD` |
| Windows 签名 | EXE 与安装包均为 `NotSigned` |

## 本机升级验证

- 已从安装中的 `0.11.0` 静默覆盖升级到 `0.11.1`。
- 安装后 EXE SHA-256 与发布目录原件一致。
- 注册表 `DisplayVersion` 为 `0.11.1`。
- 启动器设置、DPAPI 会话和 IconPark 授权文件均保留。
- 启动后目录与账号状态正常恢复，界面显示 `v0.11.1`。
- 未启动、停止或重启任何 Minecraft、Velocity、大厅、生存服或活动服。

## 分发边界

该安装包当前只存在于本机构建目录。未调用发布器、未上传 OSS、未生成公开或内部签名下载链接。若后续进入玩家灰度，必须再次核对安装包 SHA-256、来源说明、SmartScreen 提示和回滚版本，不得覆盖同名对象。

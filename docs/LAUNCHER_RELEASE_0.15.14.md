# 赫朝启动器 0.15.14 发布记录

- 发布日期：`2026-08-24`
- 正式构建源码提交：`8a47513a9e54fb349fbdf90d25ad683f055dde1a`
- 正式标签：`launcher-v0.15.14`
- 配套 API：`0.37.0-20260823T182444Z`
- 私有签名下载主机：`hechaoworld.oss-cn-shanghai.aliyuncs.com`

## 变更内容

1. 侧栏顶部和页头均改用最终品牌包中的 `75 x 37` 官方组合标，直接呈现品牌图标与
   “赫朝”字标，不再用普通界面字体拼接品牌名称；
2. 侧栏继续保留产品说明“启动器”和版本号，品牌标识、产品类型与版本信息各自独立；
3. 重写 Windows 图标生成器，按照最终 `180 x 180` SVG 的八矩形几何，为
   `16/24/32/48/64/128/256` 七个尺寸分别逐像素绘制，不再从大图抗锯齿缩放；
4. Windows 图标统一使用品牌色 `#D74735`、`#24211F`、`#FFFBF5`，同时覆盖 WPF 窗口、
   任务栏、主程序、安装器、卸载器和快捷方式；
5. 图标生成器不再默认覆盖品牌包提供的 2048px 官方 PNG，并新增 XAML、资源、ICO 目录
   结构及生成器防回归测试；
6. 完整继承 `0.15.12` 起的持久登录修复，包括 DPAPI 完整会话、有效令牌复用、临时网络
   故障保留登录态和仅明确刷新 `401` 清理会话。

品牌源压缩包 SHA-256 为
`41D4588F04DD841CDAD54C72FD769393A5E008E5F222A78D4557FD18B864C2CE`。官方界面组合标
`hechao-final-complete-logo-37h.png` 及仓库资源 SHA-256 均为
`8500734CF0BDEFA1B2995D4A21C7E647A8F8C7FAECA23D465825AF0AD5DD7B3C`；官方图标 SVG
SHA-256 为 `3ED1E4A4448D604259B9F5ECB4D5E561BDB3FF62CFD5337C9F590EED645AB8EA`；新生成的多尺寸
ICO SHA-256 为 `D37AEED7984D02CE40ED9635EA28E0BA780152C1123ECE12291DB8BBF4C7BDBB`。

本版不购买或引入 Windows 代码签名证书，安装包和主程序均保持 `NotSigned`。

## 构建与测试

| 制品 | 字节 | SHA-256 | 文件版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.14-win-x64.exe` | `61,997,617` | `E5329650A3961A39A69D41ADDBC6768AB3349227D931D0CC7D94EA4514E5274C` | `0.15.14` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,076,778` | `FAE70C35005DC061E3D46C4E0ABEDCB087E57AA0EB31C2AE7F4A38CAE72A5C9D` | `0.15.14.0` | `NotSigned` |

主程序产品版本为 `0.15.14+8a47513a9e54fb349fbdf90d25ad683f055dde1a`，确认正式制品指向
包含组合标和小尺寸图标修复的固定源码提交。

- 启动器测试：`246/246` 通过；
- 完整 .NET 解决方案：`838` 通过、`1` 项隔离 PostgreSQL 测试按环境跳过、`0` 失败；
- Release 构建和 NSIS 安装包生成成功，无警告或错误；
- 同一输入连续两次生成的 ICO 摘要一致，七个目录项尺寸与偏移均通过检查；
- XAML 只引用官方组合标，资源清单同时包含组合标、官方高分辨率图标和多尺寸 ICO；
- `0.15.13 -> 0.15.14` 覆盖升级、全新安装和两轮卸载通过，`settings.json`、
  `session.dat` 均保留，运行中的旧启动器进程没有被关闭；
- 现有后台前端未提交文件没有被暂存、覆盖、构建进启动器或混入发布提交。

## 私有 OSS 与更新通道

不可变对象：
`releases/launcher/0.15.14/Hechao-Launcher-Setup-0.15.14-win-x64.exe`。

- 首次上传成功；第二次发布读取远端对象并在长度、SHA-256 一致后跳过，上传 `0`；
- 两轮内部签名读取均为 `200`，下载长度和 SHA-256 与正式安装包一致；
- 两轮匿名读取均为 `403`；
- 私有签名地址由 OSS 原生主机提供，运行
  `Test-PrivateLauncherRelease.ps1` 时应显式传入
  `-ExpectedDownloadHost 'hechaoworld.oss-cn-shanghai.aliyuncs.com'`；
- 生产认证会话恢复成功，`0.15.13` 生成更新计划，`0.15.14` 不重复更新；完整下载安装包
  返回 `200`，长度和 SHA-256 一致；
- 公网健康、就绪、公开更新元数据、官网 `/download`、官网主页和中转站均为 `200`；
  下载页显示 `0.15.14` 和正确 SHA-256；
- 验收没有输出或写入 Git 任何签名 URL、账号身份、AccessKey、Cookie 或会话令牌。

生产更新通道已切换为 `LatestVersion=0.15.14`、
`MinimumSupportedVersion=0.12.3`，发布时间为 `2026-08-24T09:28:53Z`。环境切换前备份为
`/etc/hechao-launcher-api/environment.launcher-updates.20260824T092853Z.bak`，权限与所有者为
`600 / root:root`，SHA-256 为
`3C915BDE7D66FABB8BB9C31E7EE2BB46A60039994955283554C3C43B7F89D752`。当前环境 SHA-256 为
`B61A5078E6B644FB4389DD160AC0CF8AC046504D084C0304F779132B645A456A`。

只重启了 `hechao-launcher-api.service`。最终 API PID 为 `1898659`、`NRestarts=0`、数据库
`ready`、切换后 warning 以上日志为 `0`；Publisher PID `2011` 与 Nginx PID `958735`
保持不变，没有操作 Minecraft、Velocity 或任何游戏服。管理机上正在运行的 `0.15.13`
进程 PID `48884` 保持运行，用户下次自行重启后按正常自更新流程升级。

## 回滚

出现分发故障时恢复上述 API 环境备份并只重启 `hechao-launcher-api.service`，让尚未升级的
客户端回到 `0.15.13` 更新元数据；已经安装 `0.15.14` 的客户端不自动降级，应发布更高
版本修复。不得覆盖 `0.15.14` 的不可变 OSS 对象，也不得为启动器回滚操作游戏服。

结构化证据见
[`evidence/LAUNCHER_0.15.14_RELEASE_ACCEPTANCE_2026-08-24.json`](evidence/LAUNCHER_0.15.14_RELEASE_ACCEPTANCE_2026-08-24.json)。

# 赫朝启动器 0.15.2 发布记录

- 发布日期：2026-08-09
- 功能来源提交：`c6b211fed8f209f76e008c52c14bd1cc9b1fd214`、
  `d68933169bf4b5999aa28121c2d54a7fba7daa9d`
- 发布准备提交：`708fb595d3c35e8bc5d94d189286fb3214cc4fed`
- 正式标签：`launcher-v0.15.2`
- 生产通道切换时间：`2026-08-08T18:19:29Z`

## 变更内容

1. 主页移除重复的客户端详情区，只把回滚、校验修复、删除、客户端设置和两种 Java
   模式收进主操作右侧三点菜单。
2. 快捷设置改为两行分组控制栏，Java、运行内存和游戏目录使用 IconPark 图标、细分隔线
   与稳定的响应式宽度。
3. 主页游戏目录改为当前档案真实
   `GameData\instances\<profile-id>\.minecraft`；未选择服务器时显示明确空状态并禁用
   目录按钮，全局 `GameData` 修改入口继续只保留在设置页。
4. 官网同源活动月历、客户端下载、自更新、账号、目录过滤和游戏启动状态机保持不变。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.2-win-x64.exe` | `61,961,528` | `482BA9F5BE5CB3817B9AE39FD6C90C313B31DA809FCBE480F856EF31645A476F` | `0.15.2` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,016,345` | `621D0692C6464D769615C2326691D413F30C7182CE7C114D8CDC16D6F11CAB53` | `0.15.2+708fb595d3c35e8bc5d94d189286fb3214cc4fed` | `NotSigned` |

- Release 完整解决方案测试：`708/708`，其中 Launcher `223/223`、Publisher `55/55`。
- Release 构建为 `0` 警告、`0` 错误；PowerShell 7 合规与 `19` 项活动发布溯源检查通过。
- `1500 x 860` 与 `1060 x 640` 真实 WPF 截图通过，无横向溢出、重叠或主要操作裁切。
- 隔离安装验收：`0.15.1 -> 0.15.2`、全新安装和两轮卸载均通过；设置、DPAPI
  会话与既有正式启动器进程均保留。
- 构建使用官方 NSIS `3.12` 便携包；下载文件按 Scoop 清单公布的 SHA-1 核对后只解压到
  Git 忽略的本地工具目录，没有改变系统级软件或进入发布制品。

## 私有 OSS

- 不可变对象：
  `releases/launcher/0.15.2/Hechao-Launcher-Setup-0.15.2-win-x64.exe`。
- Publisher CLI `1.3.0` 在阿里云一次性限权 systemd 单元中首次上传；第二次核对长度、
  元数据和 SHA-256 后跳过覆盖。
- 两轮独立签名下载均为 `200`，长度和 SHA-256 与正式安装包一致；匿名读取为 `403`。
- 签名 URL 只存在于远端 `0750` 暂存目录内的 root `0600` 结果文件，验收后与
  Publisher、安装包和下载副本一并精确删除，未进入 Git、文档或终端输出。

## 生产更新通道

- `LatestVersion=0.15.2`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61961528`
- `InstallerSha256=482ba9f5be5cb3817b9ae39fd6c90c313b31da809fcbe480f856ef31645a476f`
- API 保持 `0.29.0` 与原发布目录，只重启 `hechao-launcher-api.service`；PID 从 `2063`
  变为 `105404`，`NRestarts=0`，切换后 warning/error 日志为 `0`。
- 环境文件保持 `root:root 600`；正式切换前备份为
  `/etc/hechao-launcher-api/environment.launcher-updates.20260808T181929Z.bak`，其 SHA-256
  与切换前环境文件一致。
- 内外网健康/就绪、官网、后台、中转站、公开活动和公开下载入口均通过；公开活动为
  `0` 条，下载入口返回指向 `download.hechao.world` 正式对象的 HTTPS `302`。

## 真实更新链验收

- 现有 DPAPI 会话恢复成功，生产 API 返回 `0.15.2`、最低版本 `0.12.3`、正确长度与
  SHA-256。
- `0.15.1` 生成更新计划，`0.15.2` 不生成重复更新计划。
- API 签发地址完整下载返回 `200`，共 `61,961,528` 字节，SHA-256 与发布制品一致。
- 验收工具未输出账号身份、会话令牌或签名 URL，正式启动器进程未被关闭或替换。

## 运行边界与回滚

常驻 Publisher Agent 保持 `1.2.1`、PID `2064` 和 `NRestarts=0`；本次没有启动、停止
或重启 Minecraft、Velocity、服控代理或任何游戏服务。若分发异常，恢复上述 API 环境
备份并只重启 Launcher API，或将 `LauncherUpdates__Enabled=false`。已经安装 `0.15.2`
的客户端不会自动降级；修复必须发布更高版本，不能覆盖本对象或标签。

结构化证据见
[`evidence/LAUNCHER_0.15.2_RELEASE_ACCEPTANCE_2026-08-09.json`](evidence/LAUNCHER_0.15.2_RELEASE_ACCEPTANCE_2026-08-09.json)。

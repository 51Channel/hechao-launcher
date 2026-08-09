# 赫朝启动器 0.15.3 发布记录

- 发布日期：2026-08-09
- 功能来源提交：`2b00f10b3e0a6f5531adfb21fe354e1f4ff80a0f`、
  `5ff4d59b0bca91c289d5b7c336b23aba480b0bc3`
- 发布准备提交：`012cdd3c739d98fce5bee2326f8ec9813931f94b`
- 正式标签：`launcher-v0.15.3`
- 生产通道切换时间：`2026-08-09T04:26:20Z`

## 变更内容

1. 主页内存下拉移除容易被 Popup 边界裁切的投影和系统默认虚线焦点框；“查看活动”
   按钮修正为标题行内稳定高度，不再裁切底边。
2. 皮肤头像、显示名、登录状态、访问身份和账户操作从服务器目录底部移到最左侧导航栏
   底部，中间服务器目录恢复完整滚动高度。
3. 当前服务器横幅、详情、状态、主操作和三点菜单合并为一个跨两列的连续主卡片，
   图片和详情之间不再出现外层卡片间隙。
4. 服务器目录、客户端下载、自更新、活动月历、账号和游戏启动状态机保持不变。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.3-win-x64.exe` | `61,954,842` | `EC84277585366D8C1FF1B1AB60E89AA433BF142BDC26B924E772D88F52A271CE` | `0.15.3` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,016,860` | `8EE9A773A32F509A6FAD3CF63FF2D8D4046BA4FA94207F9190862888CDD1AE37` | `0.15.3+012cdd3c739d98fce5bee2326f8ec9813931f94b` | `NotSigned` |

- Release 完整解决方案测试：`710/710`，其中 Launcher `225/225`、Publisher `55/55`。
- Release 构建为 `0` 警告、`0` 错误；XAML XML、`git diff --check`、Impeccable
  detector、PowerShell 7 和发布溯源检查通过。
- `1500 x 860` 与 `1060 x 640` WPF 截图确认账号三行信息可读，主卡片连续，无重叠、
  裁切或横向溢出。
- 隔离安装验收：`0.15.2 -> 0.15.3`、全新安装和两轮卸载均通过；设置、DPAPI
  会话与既有启动器进程均保留。
- 布局修改前生成的旧 `0.15.3` 本机制品已移入 `superseded` 隔离目录，没有上传 OSS。

## 私有 OSS

- 不可变对象：
  `releases/launcher/0.15.3/Hechao-Launcher-Setup-0.15.3-win-x64.exe`。
- Publisher CLI `1.3.0` 在阿里云一次性限权 systemd 单元中首次上传；第二次核对长度、
  元数据和 SHA-256 后跳过覆盖。
- 两轮独立签名下载均为 `200`，长度和 SHA-256 与正式安装包一致；匿名读取为 `403`。
- 签名 URL 只存在于远端 `0600` 临时结果文件；验收后连同 Publisher、安装包、配置脚本
  和下载副本精确清理，未进入 Git、文档、终端或 journal。

## 生产更新通道

- `LatestVersion=0.15.3`
- `MinimumSupportedVersion=0.12.3`
- `InstallerBytes=61954842`
- `InstallerSha256=ec84277585366d8c1ff1b1ab60e89aa433bf142bdc26b924e772d88f52a271ce`
- API 保持 `0.29.0` 与原发布目录，只重启 `hechao-launcher-api.service`；PID 从
  `105404` 变为 `418090`，`NRestarts=0`，切换后 warning/error 日志为 `0`。
- 环境文件保持 `root:root 600`；正式切换前备份为
  `/etc/hechao-launcher-api/environment.launcher-updates.20260809T042620Z.bak`，其 SHA-256
  与切换前环境文件一致。
- 内外网健康/就绪、官网、后台、中转站、公开活动和公开下载入口均通过；公开活动为
  `0` 条，下载入口返回指向 `download.hechao.world` 正式对象的 HTTPS `302`。

## 真实更新链验收

- 现有 DPAPI 会话恢复成功，生产 API 返回 `0.15.3`、最低版本 `0.12.3`、正确长度与
  SHA-256。
- `0.15.2` 生成更新计划，`0.15.3` 不生成重复更新计划。
- API 签发地址完整下载返回 `200`，共 `61,954,842` 字节，SHA-256 与发布制品一致。
- 验收工具未输出账号身份、会话令牌或签名 URL，没有关闭或替换正式启动器进程。

## 运行边界与回滚

常驻 Publisher Agent 保持 `1.2.1`、PID `2064` 和 `NRestarts=0`；本次没有启动、停止
或重启 Minecraft、Velocity、服控代理或任何游戏服务。若分发异常，恢复上述 API 环境
备份并只重启 Launcher API，或将 `LauncherUpdates__Enabled=false`。已经安装
`0.15.3` 的客户端不会自动降级；修复必须发布更高版本，不能覆盖本对象或标签。

结构化证据见
[`evidence/LAUNCHER_0.15.3_RELEASE_ACCEPTANCE_2026-08-09.json`](evidence/LAUNCHER_0.15.3_RELEASE_ACCEPTANCE_2026-08-09.json)。

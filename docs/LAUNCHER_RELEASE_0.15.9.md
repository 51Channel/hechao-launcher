# 赫朝启动器 0.15.9 发布记录

- 发布日期：2026-08-17
- 正式构建源码提交：`906fdb848eb3805e05b95d01b569f32379fd3cd4`
- 正式标签：`launcher-v0.15.9`
- 配套 API：`0.33.1-20260817T031438Z`
- 生产通道切换时间：`2026-08-17T14:42:37Z`

## 变更内容

1. 客户端更新按 SHA-256 复用当前档案和对象缓存。只有本地与缓存都缺失的唯一对象才
   进入网络队列，重复引用同一对象时只下载一次。
2. 下载页分开显示本地检查、增量下载、客户端准备、版本切换和 Java 准备。本地磁盘
   校验不再显示成网络下载速度。
3. 增量下载阶段只显示真实缺失对象的字节总量。档案完整大小继续用于本地检查、磁盘
   规划和完整性校验，不再暗示本次需要重下整包。
4. 工业季 `1.0.11 -> 1.0.12` 的 `4,456` 个共同文件保持不变，实际只需要取得一个
   `27,450` 字节的新 JAR；本次没有修改工业季清单或对象。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.9-win-x64.exe` | `61,995,538` | `88AE1869A47E313291AF0C60F8984BF14C1B5D0719291D72DDA254870487DE79` | `0.15.9` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,053,206` | `8B703CD1A02DEF6938586516FA69A2A1493C5B75B57F3E0818F4972BD81CAD1F` | `0.15.9+906fdb848eb3805e05b95d01b569f32379fd3cd4` | `NotSigned` |

- `.NET SDK 10.0.400` Release 构建完成，安装包和 EXE 均为 `0.15.9`。
- 完整解决方案 `806` 项通过；`1` 项需要独立 PostgreSQL 环境的集成测试按条件跳过。
  Distribution `47/47`、Launcher `234/234`，改动文件格式、XAML/XML、PowerShell 7
  合规和 `git diff --check` 通过。
- `0.15.8 -> 0.15.9` 隔离覆盖安装、全新安装和两轮卸载均通过；设置、DPAPI 会话文件
  和验收开始时的既有启动器进程均保留。

## 私有 OSS 与更新通道

- 不可变对象：
  `releases/launcher/0.15.9/Hechao-Launcher-Setup-0.15.9-win-x64.exe`。
- 首次发布成功；第二次发布核对长度、版本、文件名和 SHA-256 后跳过，没有覆盖对象。
- 两轮独立签名下载均为 `200`，长度和 SHA-256 一致；匿名读取均为 `403`。签名 URL
  没有进入 Git、文档、结构化证据或面向用户的输出。
- 官网公开下载网关完整下载 `61,995,538` 字节并通过 SHA-256；公开元数据、官网、
  管理站、中转站、API 健康和数据库就绪端点均为 `200`。
- 生产更新通道为 `LatestVersion=0.15.9`、`MinimumSupportedVersion=0.12.3`。认证会话
  确认 `0.15.8` 生成更新计划、`0.15.9` 不重复更新，并通过 API 签发地址完整下载。

## 生产切换与回滚

- 切换前环境备份：
  `/etc/hechao-launcher-api/environment.launcher-updates.20260817T144238Z.bak`，
  SHA-256 `F70F15EABCD2DCEDD1809DEEADB200D253500D4D5445D946C3C12F1A4F1E57D`。
- 更新后环境 SHA-256 为
  `DBBDD10DD672C163247ACFA685E3752CBD87C3555B80B4B3D1E72B71E14E66A7`；配置和备份均为
  `root:root 600`。
- 只重启 `hechao-launcher-api.service`，API PID 从 `2215592` 变为 `2567946`，
  `NRestarts=0`；Publisher PID `2064` 与 Nginx PID `1742715` 未变化，切换后
  warning/error/critical 日志计数为 `0`。
- 没有修改 PostgreSQL、客户端档案、活动企划、Minecraft 服务端、Velocity、
  Publisher、Nginx 或服控代理，也没有执行任何游戏服启停和控制台命令。
- 如更新分发异常，恢复上述环境备份并只重启 Launcher API，或禁用启动器更新通道。
  已安装 `0.15.9` 的启动器不会自动降级；后续修复必须发布更高版本。

结构化证据见
[`evidence/LAUNCHER_0.15.9_RELEASE_ACCEPTANCE_2026-08-17.json`](evidence/LAUNCHER_0.15.9_RELEASE_ACCEPTANCE_2026-08-17.json)。

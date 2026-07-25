# 赫朝启动器 0.10.0 候选记录

> 候选日期：`2026-07-24`
>
> 源码标签：`launcher-v0.10.0`
>
> 启动器源码提交：`9cba23e9d0b5ba799af50dcc2ef0018cfe5a31e4`
>
> 发布前恢复测试提交：`89c823370a68898baaf46a488187fbf53a5fd3d1`
>
> 状态：已上传私有 OSS，仅开放短时内部灰度下载，尚未公开发布

## 主要内容

- 五工作区启动器界面、IconPark 官方图标和苹方优先字体。
- 赫朝账号登录、Microsoft 正版绑定、DPAPI 会话恢复与 LuckPerms 等级目录。
- 每个客户端档案独立 `.minecraft`，共享对象、受管 Java、断点续传、校验修复和原子切换。
- Fabric 基础档案与 NeoForge 活动档案的进程构建和 Velocity 启动授权。
- 退出当前设备、退出所有设备和密码确认解除 Minecraft 绑定。
- 本地退出记录与玩家主动生成的脱敏诊断包。

## 候选制品

| 项目 | 值 |
| --- | --- |
| 启动器 EXE | `artifacts/publish/win-x64/Hechao.Launcher.exe` |
| EXE 大小 | `68,607,280` 字节 |
| EXE SHA-256 | `D9FA21C5F15E3B30FFED8FEF4E011672B75C4A15987712BBF574A0CEDD3834F3` |
| EXE FileVersion | `0.10.0.0` |
| EXE ProductVersion | `0.10.0+9cba23e9d0b5ba799af50dcc2ef0018cfe5a31e4` |
| 安装包 | `artifacts/installer/Hechao-Launcher-Setup-0.10.0-win-x64.exe` |
| 安装包大小 | `61,796,065` 字节 |
| 安装包 SHA-256 | `E2E14306882EF072016F35D740D2F06A7C8D12F63FFE28DD0F6A2C07B24D4876` |
| Windows 签名 | EXE 与安装包均为 `NotSigned` |

安装包内的启动器 EXE 哈希与独立构建原件一致。程序目录同时包含 IconPark 的 `LICENSE` 与 `NOTICE.md`，不包含 PDB、账号缓存、会话、游戏文件、日志或诊断包。

## 本机验收

- 完整解决方案测试：`157/157` 通过。
- 格式检查：通过。
- `0.9.1` 安装后原地升级到 `0.10.0`：通过。
- `0.10.0` 干净安装：通过。
- 安装后 EXE FileVersion、ProductVersion 和 SHA-256：通过。
- IconPark 授权文件安装：通过。
- `%LocalAppData%\Hechao\Launcher` 与 `GameData` 在升级和卸载后保留：通过。
- 静默卸载后程序目录、开始菜单、应用注册表和卸载注册表无残留：通过。
- 过期 OSS 下载链接重新向 API 获取后继续 Range 下载：通过。
- OSS 暂时不可用时重试三次、保留当前可用客户端并清理暂存目录：通过。
- 被篡改受管文件重新下载修复，并保留上一完整目录：通过。

验收过程没有启动 Minecraft、Velocity 或任何游戏服务器，临时安装目录、测试注册表项、快捷方式和测试数据标记均已清理。

## 私有分发验收

- 对象键：`releases/launcher/0.10.0/Hechao-Launcher-Setup-0.10.0-win-x64.exe`。
- 发布 RAM：`HechaoLauncherOssObjectPublish` v3，仅有 `objects/*` 与 `releases/launcher/*` 的 `GetObject/PutObject`。
- 上传工具：`Hechao.Publisher 0.8.1`，标签 `publisher-v0.8.1`。
- 上传后远端长度与 SHA-256、版本、原文件名元数据回读一致。
- 无签名永久直链返回 `403`。
- 24 小时签名链接完整下载后的大小和 SHA-256 与候选制品一致。
- 第二次执行只校验并跳过既有对象，没有重复上传或覆盖。
- 短时链接本身不写入 Git、文档或公开网页；当前只供内部灰度。

## 上线前置条件

以下条件尚未完成，因此本候选不能写成“已向玩家发布”：

1. [许可已完成] 使用真实正版账号完成绑定、下载、启动和进服验收。
2. [已完成] 管理员单独重启 Velocity，已放置的授权插件确认以 `monitor` 模式加载。
3. 核对所有 Velocity 目标、NPC 转服、`/hub` 和替换服映射。
4. 完成管理员与 2 至 3 名内部成员灰度后，再决定 5 人与 20 人范围。
5. 对外提供未签名安装包时，公告必须同时给出官方来源、大小、SHA-256 和 SmartScreen 提示。

本次已按单独确认完成私有安装包上传和内部短时下载。建立公开下载地址、扩大灰度或向全部玩家发布仍属于新的生产分发动作，必须再次确认。

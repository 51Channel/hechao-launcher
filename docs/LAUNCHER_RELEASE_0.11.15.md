# 赫朝启动器 0.11.15 发布记录

> 构建日期：`2026-07-28`
>
> 启动器制品源码提交：`5e0943ae291f1e0eb48bf7e1e07809e62a4972dc`
>
> Git 标签：`launcher-v0.11.15`
>
> 配套 API：`0.20.2-20260727T225819Z`
>
> 状态：私有 OSS 灰度候选已验证；替换 `0.11.14`

## 变化

- Minecraft 启动准备现在会检查版本元数据声明的 Mojang Log4j 配置文件。
- 缺失或损坏的配置会从元数据中的 HTTPS 地址下载，按长度和 SHA-1 校验后原子替换，
  避免游戏因 `assets/log_configs/*.xml` 缺失而立即退出。
- 同时兼容 Mojang/CmlLib 返回的叶文件名与 `assets/log_configs/...` 两种路径形式。
- 路径越界、绝对路径、非 HTTPS 地址、异常大小、哈希格式错误或内容不匹配均会硬失败，
  且不留下临时文件。
- 新增安全发布包装脚本，签名下载链接只写入管理员 ACL 保护目录，终端仅显示非秘密
  发布状态、对象键、到期时间、大小和 SHA-256。

## 验收证据

- 完整解决方案测试为 `365/365`，其中启动器测试为 `95/95`。
- 新增 5 个测试覆盖首次下载、已验证文件复用、损坏文件原子替换、两种合法路径和
  越界/内容篡改拒绝。
- 使用已安装 Activity `1.0.10` 档案执行真实启动准备冒烟，在创建 Minecraft 进程前
  主动中止；成功生成 `client-1.21.2.xml`，大小 `1,073` 字节，SHA-1 为
  `39384bd14c0606d812afec88d8aff595b2587dd9`，没有启动 Activity Java 进程或遗留
  临时文件。
- `tools/Test-WindowsInstaller.ps1` 完成 `0.11.14 -> 0.11.15` 覆盖升级、
  `0.11.15` 干净安装和两轮静默卸载。
- 升级后的 EXE FileVersion 为 `0.11.15.0`，ProductVersion 为
  `0.11.15+5e0943ae291f1e0eb48bf7e1e07809e62a4972dc`。
- 注册表 DisplayVersion、开始菜单目标、IconPark `LICENSE` 与 `NOTICE.md` 均通过。
- 安装前后的 `settings.json` 与 DPAPI `session.dat` SHA-256 保持一致，原有启动器
  进程未被关闭。

## 制品

| 项目 | 值 |
| --- | --- |
| 启动器 EXE | `artifacts/publish/win-x64/Hechao.Launcher.exe` |
| EXE 大小 | `68,763,203` 字节 |
| EXE SHA-256 | `5BF250BF7E11806B1A81AFB335BA589B7C33F6791009DF161FE2273D46AD1433` |
| 安装包 | `artifacts/installer/Hechao-Launcher-Setup-0.11.15-win-x64.exe` |
| 安装包大小 | `61,867,426` 字节 |
| 安装包 SHA-256 | `3C9139F8F7853C370C83A14537916D73258123A8E1CB26FDBA0B0EECD3219E44` |
| Windows 签名 | EXE 与安装包均为 `NotSigned` |

## 私有灰度发布

安装包已写入不可变对象：

```text
releases/launcher/0.11.15/Hechao-Launcher-Setup-0.11.15-win-x64.exe
```

第二次发布确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`；
24 小时 OSS Bucket 原始节点签名下载返回 `200`，完整下载为 `61,867,426`
字节，SHA-256 与本机制品一致，耗时约 `1.46` 秒。签名链接只保存在当前管理员账户
的 ACL 保护目录，没有写入 Git、文档或公告。

## 回滚

若灰度发现独立阻断，关闭启动器后使用 `0.11.14` 覆盖安装。覆盖不会删除赫朝会话、
设置、客户端、受管 Java、下载缓存或世界存档。回滚后可能再次遇到缺失 Mojang
日志配置导致的启动失败，应保留 `0.11.15` 安装包和诊断证据以便恢复。

## 边界

本次启动器制品只修改启动前日志配置准备，不修改 API、数据库、Velocity、客户端
档案或服务器目录。PVP CrossStitch 服务端兼容修复单独记录在
[`evidence/CLIENT_ACTIVITY_PVP_ROUTE_AND_PVP_FIX_2026-07-28.json`](evidence/CLIENT_ACTIVITY_PVP_ROUTE_AND_PVP_FIX_2026-07-28.json)；
它不属于本安装包的构建内容。

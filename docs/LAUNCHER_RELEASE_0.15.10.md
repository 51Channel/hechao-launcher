# 赫朝启动器 0.15.10 发布记录

- 发布日期：`2026-08-24`
- 正式构建源码提交：`eece13dec1c46ff4d70125fb71277c673de522b3`
- 正式标签：`launcher-v0.15.10`
- 配套 API：`0.37.0-20260823T182444Z`
- 更新下载主机：`download.hechao.world`

## 变更内容

1. 刷新启动器中的真实服务器状态展示，避免目录状态长期停留在旧缓存；
2. 完善注册页面和 Microsoft 正版登录后的注册流程；
3. 保留现有客户端档案、Java 管理、代理开关、增量下载和玩家设置隔离逻辑；
4. 私有发布验收脚本改为使用显式下载主机参数，避免把新下载网关误判为旧 OSS 原始域名。

本版不购买或引入 Windows 代码签名证书，安装包和主程序均保持 `NotSigned`。这属于已确认
的项目决策，不把未签名状态伪装成已签名。

## 构建与测试

| 制品 | 字节 | SHA-256 | 文件版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.10-win-x64.exe` | `61,997,588` | `6E3E449BA1FF59EF3B3AEA1EFD627F30A03E769BC57484033EA8D326AFCD4CC5` | `0.15.10` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,054,737` | `536BB058CBBA1C83F73D52A5E52B2B37687E5EB2ABE1567AAAAA06F247593464` | `0.15.10.0` | `NotSigned` |

主程序产品版本为 `0.15.10+eece13dec1c46ff4d70125fb71277c673de522b3`。

- 完整 .NET 解决方案：`828` 通过、`1` 项隔离 PostgreSQL 测试按环境跳过、`0` 失败；
- Release 构建：`0` 错误、`0` 警告；
- 启动器、API、发布器、服控和分发测试及 PowerShell 7 合规检查通过；
- 现有后台前端未提交文件不属于本发布提交，发布时没有覆盖或混入这些用户改动。

## 私有 OSS 与更新通道

不可变对象：
`releases/launcher/0.15.10/Hechao-Launcher-Setup-0.15.10-win-x64.exe`。

- 首次发布成功；第二次发布先校验对象，上传 `0`，没有覆盖不可变对象；
- 两轮内部签名读取均返回 `200`，长度和 SHA-256 与制品一致；
- 匿名读取返回 `403`；
- 认证自更新回归恢复现有会话成功，旧版 `0.15.9` 生成更新计划，当前版 `0.15.10` 不重复
  提示；实际下载返回 `200`，61,997,588 字节和 SHA-256 一致；
- 验收结果没有保存或输出签名 URL、账号身份、AccessKey、Cookie 或会话令牌。

API 更新通道已为 `LatestVersion=0.15.10`、`MinimumSupportedVersion=0.12.3`。发布只更新
启动器 API 的更新元数据，不操作 Minecraft、Velocity 或其他游戏进程。

## 回滚

启动器安装包对象不可覆盖，出现问题时发布更高版本修复，不能把 `0.15.10` 对象替换成旧
内容。分发 API 异常时恢复 API 的上一版本和环境备份，只重启
`hechao-launcher-api.service`；不降级或操作任何游戏服。

结构化证据见
[`evidence/LAUNCHER_0.15.10_RELEASE_ACCEPTANCE_2026-08-24.json`](evidence/LAUNCHER_0.15.10_RELEASE_ACCEPTANCE_2026-08-24.json)。

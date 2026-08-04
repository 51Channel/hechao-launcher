# Publisher Agent 1.1.0

> 状态：已在阿里云接管生产任务；Windows 回滚实例已停止并完整保留。
>
> 范围：只迁移整合包客户端 Publisher Agent；不升级 API、不修改 OSS 对象、不启停
> Minecraft、Velocity 或 owl5 服务端。

## 变更

- 保留既有 Windows DPAPI `CurrentUser` 模式，新增 Linux
  `systemd-credentials` 模式。
- systemd 配置只保存三个凭据名。令牌、P-256 签名私钥和 Publisher 专用 OSS
  AccessKey 由 `systemd-creds --with-key=host` 加密，运行时只出现在服务私有的
  `$CREDENTIALS_DIRECTORY`。
- 新增 Linux 离线 `validate-package-agent`，在不访问 API 或 OSS 的情况下检查配置、
  令牌、签名私钥和 OSS 凭据。
- 领取任务后按压缩包大小、可配置展开倍率和最低剩余空间计算工作空间。空间不足时保持
  心跳和租约等待，不开始下载或解压。
- 新增独立服务账户、资源上限、`ProtectSystem=strict`、空能力集和重启恢复的 systemd
  单元，以及保留旧状态的安装/回滚脚本。
- 新增 PowerShell 7 凭据迁移器。DPAPI 明文只存在于进程内存和 SSH 标准输入，不写入
  Git、命令参数、日志或中间文件。

## 正式制品

| 项目 | 值 |
| --- | --- |
| RID | `linux-x64` |
| 类型 | 自包含单文件 |
| 大小 | `74,602,011` 字节 |
| SHA-256 | `599A068E07872A6E655AF034B110F751F0547C557A0C83688B433D77150F6928` |

制品由当前候选源码使用仓库固定的 .NET 10 SDK 重建：

```powershell
dotnet publish src\Hechao.Publisher\Hechao.Publisher.csproj `
  -c Release -r linux-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishTrimmed=false `
  -p:DebugType=None -p:DebugSymbols=false
```

## 已完成验证

- Publisher 单元测试：`48/48`。
- API 兼容测试：`268/268`，代码最终未要求升级 API。
- PowerShell 7 AST 解析通过。
- Linux 单文件在阿里云隔离目录完成离线凭据校验。
- systemd 为非 root 服务账号提供的 `0440` 运行时凭据已兼容，other 可读权限仍被拒绝。
- 宽权限凭据文件被拒绝，恢复为 `0600` 后通过。
- 使用假的 Windows DPAPI 输入完成
  `DPAPI -> SSH stdin -> systemd-creds -> systemd-run` 全链路预检；测试凭据随后删除。
- systemd 单元 `systemd-analyze verify` 通过，安全暴露评分 `2.9 OK`。

## 生产部署

- 正式标签：`publisher-v1.1.0`。
- 功能提交：`2f41d25ba2ac490cedb63e381c51c8cf048cb3bb`；systemd `0440`
  兼容修复提交：`3f20007308e33ac8ea3c7eb2b7564241a44f730b`。
- 生产主机：`8.148.207.171`；发布目录：
  `/opt/hechao-package-publisher/releases/1.1.0-3f20007`。
- `hechao-package-publisher.service` 为 `active/enabled`，运行用户为
  `hechao-publisher`；正式二进制大小和 SHA-256 与上表一致。
- 数据库心跳版本为 `1.1.0`。验收时队列只有 `Completed=1`、`Failed=3`，不存在
  `QueuedForPublishing` 或 `PublishingClient`。
- API `/healthz` 与 `/readyz` 均为 `200`；Publisher 错误级 journal 为 `0`，秘密特征
  扫描命中为 `0`。
- systemd 手工重启恢复通过，PID 从 `4027576` 变为 `4028581`，重启后心跳继续更新。
- Windows `Hechao Launcher Package Publisher Agent` 计划任务为 `Ready`，本机进程
  数为 `0`，EXE、配置和三份 DPAPI 输入继续保留作为回滚。
- 本次没有升级 API、修改 OSS 对象或操作 Minecraft、Velocity、owl5 游戏服务。

脱敏结构化证据见
[`evidence/PUBLISHER_1.1.0_ALIYUN_MIGRATION_2026-08-05.json`](evidence/PUBLISHER_1.1.0_ALIYUN_MIGRATION_2026-08-05.json)。

## 生产切换门槛

1. 当前发布队列不存在 `QueuedForPublishing` 或 `PublishingClient`。
2. 把生产 DPAPI 输入迁移为阿里云主机绑定的 encrypted credentials，并通过离线校验。
3. 先安装但不启用 Linux 服务，再次核对制品、配置、磁盘和 API 健康。
4. 停止本机 Windows 计划任务后启动 Linux 服务；禁止双实例重叠领取任务。
5. 验证 `1.1.0` 心跳、API 健康、journal、重启恢复和本机回滚路径。

失败时停止并禁用 Linux 服务，确认没有活动租约，再恢复本机
`Hechao Launcher Package Publisher Agent` 计划任务。Windows EXE、配置和 DPAPI 文件
在生产验收后仍保留，不删除。

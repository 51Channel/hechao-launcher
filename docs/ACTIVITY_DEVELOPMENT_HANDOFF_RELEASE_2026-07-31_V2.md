# 活动开发交接包 v2 发布记录（2026-07-31）

## 1. 结果

活动开发交接包 v2 已生成并完成收件人视角验收。本版新增赫朝新服务端基础组件规范、
机器可读组件计划样例和“当前无活动名称、源码或目标”的纯规范接管模式，并把对应要求
接入需求单、首条消息、开发指南、验收清单、交付报告和常见错误。

交接包同时增加 Velocity Authorizer、内部大厅组件、主机服控、状态采集、三类深度指标、
世界备份和 Fabric forwarding 的源码与无秘密部署参考。它不把这些组件定义成一个可
复制的 `plugins` 目录，而是按代理单例、内部大厅、VPS 主机和后端加载器分层。

本次没有连接、启动、停止、重启或修改任何生产游戏服，也没有修改后台目录记录。主
工作区中并行存在的 API 改动未进入来源提交或交接包。

## 2. 来源

| 字段 | 值 |
| --- | --- |
| 仓库 | `51Channel/hechao-launcher` |
| 分支 | `main` |
| 来源提交 | `679d9d1bb419549d5dfac0487546e577b7539356` |
| 注释标签 | `handoff-activity-development-2026-07-31-v2` |
| 来源提交与标签已推送 | 是 |
| 来源工作树 | 独立 detached clean worktree |
| `sourceDirty` | `false` |
| PowerShell | `7.6.4` |

交接包源文件位于 [`handoff/activity-development`](../handoff/activity-development/README.md)，
基础组件规范和样例分别是
[`HECHAO_NEW_SERVER_BASELINE.md`](HECHAO_NEW_SERVER_BASELINE.md) 与
[`component-plan.example.json`](examples/server-baseline/component-plan.example.json)。

## 3. 正式制品

| 字段 | 值 |
| --- | --- |
| ZIP | `Hechao-Activity-Development-Handoff-2026-07-31-679d9d1bb4.zip` |
| 本机目录 | `H:\hechao Launcher\artifacts\handoff` |
| ZIP 字节数 | `943846` |
| ZIP SHA-256 | `55c2c7fd44529a071864af6138b49ede790f8cfe582c859c898ae2cea66b5eb1` |
| 旁车 | 同名 `.zip.sha256` |
| 有效载荷文件 | `370` |
| `MANIFEST.json` 条目 | `371` |
| 验收文件 | `373` |
| 解压后验收字节数 | `2140999` |

v1 制品
`Hechao-Activity-Development-Handoff-2026-07-31-3d31feb186.zip` 及其旁车保持原样。
v2 使用新的来源提交、文件名、哈希和注释标签，没有覆盖旧正式包。

## 4. 验证结果

- PowerShell 7 全仓解析：通过，`41` 个脚本。
- 发布来源台账：通过，`18` 条既有活动发布记录完整。
- 两份新增 JSON 解析、映射源和映射目标唯一性：通过。
- `14` 份本次入口/规范 Markdown 的本地链接与代码围栏：通过。
- 高置信秘密特征、禁止扩展名、危险目录和重解析点扫描：通过。
- ZIP 同名旁车、`MANIFEST.json`、`SHA256SUMS` 和逐文件哈希：`373/373` 通过。
- 独立解压目录和包内自带校验器：`373/373` 通过。
- 新增源码、测试、部署和工具映射均在 ZIP 中存在。
- 篡改拒绝：修改解压后的 `README.md` 后，以 `SHA256SUMS` 不一致拒绝。
- 路径穿越拒绝：包含 `Hechao-Bad/../escape.txt` 的测试 ZIP 在解压前被拒绝。

正式 ZIP 验收命令：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Test-ActivityDevelopmentHandoff.ps1 `
  -ArchivePath .\artifacts\handoff\Hechao-Activity-Development-Handoff-2026-07-31-679d9d1bb4.zip
```

## 5. 交付方式

向同事同时发送 v2 ZIP 和同名 `.sha256`，让 Codex 从解压根目录开始。当前还没有活动
名称、源码或目标时，使用 `01-给Codex的首条消息.md` 的“模式 A：当前只有规范”；
拿到真实活动后再使用模式 B，填写需求单和组件计划。

生产凭据、VPS 登录材料、世界、日志、数据库和活动玩法源码不在交接包内。未来生产
写操作仍以当次明确授权、实时核验、兼容实现和可恢复备份为前提。

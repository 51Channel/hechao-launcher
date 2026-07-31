# 活动开发交接包发布记录（2026-07-31）

## 1. 结果

用于同事及其 Codex 的独立中文交接包已生成并完成收件人视角验收。交接包不依赖原聊天
记录或个人记忆库，包含活动通道规范、操作流程、需求模板、验收清单、最终报告模板、
常见错误、轻量案例、完整 `docs/` 快照，以及目录、分发和服控关键代码参考。

本次没有连接、启动、停止、重启或修改任何生产游戏服，也没有修改后台目录记录。

## 2. 来源

| 字段 | 值 |
| --- | --- |
| 仓库 | `51Channel/hechao-launcher` |
| 分支 | `main` |
| 来源提交 | `3d31feb186764edee6e10f02fa424313d65a553b` |
| 注释标签 | `handoff-activity-development-2026-07-31`（落点为来源提交） |
| 来源提交已推送 | 是 |
| 来源工作树 | 独立 detached clean worktree |
| `sourceDirty` | `false` |
| PowerShell | `7.6.4` |

交接包源文件位于 [`handoff/activity-development`](../handoff/activity-development/README.md)，
构建和独立验收入口分别是
[`New-ActivityDevelopmentHandoff.ps1`](../tools/New-ActivityDevelopmentHandoff.ps1) 与
[`Test-ActivityDevelopmentHandoff.ps1`](../tools/Test-ActivityDevelopmentHandoff.ps1)。

## 3. 正式制品

| 字段 | 值 |
| --- | --- |
| ZIP | `Hechao-Activity-Development-Handoff-2026-07-31-3d31feb186.zip` |
| 本机目录 | `H:\hechao Launcher\artifacts\handoff` |
| ZIP 字节数 | `725855` |
| ZIP SHA-256 | `d1322369bc3f4469adcabee1208d02f3ee06d2c57caf35717372ab3408caa212` |
| 旁车 | 同名 `.zip.sha256` |
| 有效载荷文件 | `260` |
| `MANIFEST.json` 条目 | `261`（有效载荷加 `PACKAGE-INFO.json`） |
| 验收文件 | `263`（再含 `MANIFEST.json` 和 `SHA256SUMS`） |
| 解压后验收字节数 | `1631075` |

ZIP 和 `.sha256` 属于本机交付制品，受 `.gitignore` 保护，不进入 Git。正式制品不可覆盖；
内容变化后应从新的干净提交生成新文件名和新哈希。

## 4. 验证结果

- PowerShell 7 全仓脚本解析：通过，`41` 个脚本。
- 发布来源台账：通过，`17` 条既有活动发布记录完整。
- 高置信秘密特征扫描：通过。
- 包内危险目录、禁止扩展名、符号链接和路径穿越检查：通过。
- ZIP 同名旁车 SHA-256：通过。
- ZIP 内逐文件长度与 SHA-256：`263/263` 通过。
- 解压目录再次校验：`263/263` 通过。
- `MANIFEST.json`、`SHA256SUMS` 与实际文件集合：完全一致。
- 人类/Codex 入口、总规范和轻量案例共 `11` 份 Markdown：本地链接与代码围栏通过。
- 篡改拒绝：修改解压后的 `README.md` 后，验收器以 SHA-256 不一致拒绝。
- 路径穿越拒绝：包含 `Package/../escape.txt` 的测试 ZIP 被拒绝。

正式 ZIP 验收命令：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Test-ActivityDevelopmentHandoff.ps1 `
  -ArchivePath .\artifacts\handoff\Hechao-Activity-Development-Handoff-2026-07-31-3d31feb186.zip
```

## 5. 交付方式

向同事同时发送 ZIP 和同名 `.sha256`。同事先验证 ZIP，再解压到新目录，并让 Codex 从
解压后的根目录开始，以便自动读取根 `AGENTS.md`。首条任务消息使用
`01-给Codex的首条消息.md`，新活动先填写 `02-新活动需求单模板.md`，第一次接入按
`docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md` 演练。

生产凭据、VPS 登录材料、世界、日志、数据库和活动玩法源码不在交接包内；真实开发时
另行提供活动源码仓库，生产写操作仍以当次明确授权和实时核验为准。

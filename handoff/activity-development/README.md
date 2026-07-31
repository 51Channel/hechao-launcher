# 赫朝 Minecraft 活动开发交接包

本包供赫朝项目内部开发者和 Codex 使用，目标是让新的开发环境在不依赖历史聊天记录、
个人记忆库或生产凭据的前提下，理解并延续当前活动客户端与服务端框架。

## 先做什么

1. 人类负责人先阅读 [`00-从这里开始.md`](00-从这里开始.md)。
2. 让 Codex 从本目录启动，它会自动读取根目录 [`AGENTS.md`](AGENTS.md)。
3. 把 [`01-给Codex的首条消息.md`](01-给Codex的首条消息.md) 填好后发给 Codex。
4. 使用 [`02-新活动需求单模板.md`](02-新活动需求单模板.md) 固定玩法、版本和边界。
5. 实施前完整阅读活动通道总规范和
   [`docs/HECHAO_NEW_SERVER_BASELINE.md`](docs/HECHAO_NEW_SERVER_BASELINE.md)。
6. 第一次接入先走
   [`docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md`](docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md)
   的轻量案例，不直接拿生产活动练手。

## 当前最重要的六条规则

- 所有玩家活动统一使用 Velocity 目标 `activity`。
- owl5 `127.0.0.1:25568` 属于 `owl5-activity-slot`，同一时刻只运行一个活动后端。
- 新服务端先建立组件计划；不得复制大厅、Survival 或旧活动服的完整插件/模组目录。
- 不同 Minecraft 版本、加载器或独立模组集合使用独立客户端档案和可写 `.minecraft`。
- 启动器是唯一换服入口，不添加 `/hub`、大厅 NPC、自动回大厅或代理失败回退。
- 部署默认保持停服；没有当前任务的明确授权，不启动、重启或切换生产后端。

## 包内内容

```text
00-从这里开始.md                 人类负责人入口
01-给Codex的首条消息.md          可直接使用的任务提示词
02-新活动需求单模板.md           开发前需求与标识模板
03-如何基于现有框架开发.md       架构讲解与决策流程
04-开发与上线验收清单.md         从代码到 20 人灰度的检查单
05-最终交付报告模板.md           Codex 完成任务时的固定报告格式
06-常见错误与处理.md             既有故障和禁止做法
AGENTS.md                        本包内 Codex 强制指令
docs/                            当前仓库规范、案例、发布记录和历史证据快照
src/                             与目录、档案、分发、服控、授权和基础组件有关的代码参考
tests/                           相关自动测试参考
deploy/                          无秘密服控、指标、forwarding、备份和主机接入参考
tools/                           客户端档案准备和规范校验工具
PACKAGE-INFO.json                来源提交和生成信息
MANIFEST.json                    每个交接文件的长度与 SHA-256
SHA256SUMS                       包内文件校验表
```

## 信息优先级

出现冲突时按以下顺序处理：

1. 当前任务中用户的明确要求；
2. 实施当天对源码、服务器、端口、进程、后台和备份的实时核验；
3. `AGENTS.md` 和 `docs/ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md`；
4. 其他当前运维规范；
5. 发布记录与 `docs/evidence` 中的历史快照。

历史证据只能说明某个日期发生过什么，不能证明当前生产仍是同一版本或状态。

## 本包不包含

- VPS 密码、SSH 私钥、AccessKey、令牌、Cookie、验证码或任何身份材料；
- Minecraft 世界、玩家数据、日志、崩溃转储或生产数据库；
- 可直接执行的生产授权；
- 活动玩法源码仓库本身。

实施时必须另外提供实际活动源码仓库路径或 Git 地址。不要在本交接包内开发后把它当成
正式源码仓库，也不要把包内示例 JSON 直接上传到生产 API。

## 完整性

收到 ZIP 后先核对同名 `.sha256` 文件。解压后可使用包内
`tools/Test-ActivityDevelopmentHandoff.ps1` 再次验证清单。任何校验失败都应重新取得
交接包，不继续施工。

```powershell
# 收包时直接验证 ZIP、同名旁车哈希和全部包内文件
pwsh -NoLogo -NoProfile -File .\tools\Test-ActivityDevelopmentHandoff.ps1 `
  -ArchivePath .\Hechao-Activity-Development-Handoff-<日期>-<提交>.zip

# 解压后，在交接包根目录再次验证逐文件清单
pwsh -NoLogo -NoProfile -File .\tools\Test-ActivityDevelopmentHandoff.ps1 `
  -PackageRoot .
```

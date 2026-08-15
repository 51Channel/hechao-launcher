# 赫朝平台项目指令

本文件适用于本仓库中的全部 Codex 任务。当前用户要求、实时验证结果和更具体目录中的
`AGENTS.md` 优先于本文件。

## 任务开始

1. 使用 PowerShell 7（`pwsh`），不要使用 Windows PowerShell 5.1 执行业务脚本。
2. 先运行 `git status --short --branch`，保护已有未提交改动；不得回退或混入他人的改动。
3. 先读 `README.md`，再读取与任务直接有关的 `docs` 文档。生产路径、端口、版本、
   进程和服务状态属于易变事实，写操作前必须实时核验。
4. 密码、令牌、AccessKey、Cookie、私钥、验证码和身份材料不得进入源码、日志、命令
   输出、文档、提交或聊天总结。

## 活动客户端与服务端

凡涉及 Minecraft 活动、活动客户端档案、活动服部署或活动切换，必须先读取：

- `docs/ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md`
- `docs/HECHAO_NEW_SERVER_BASELINE.md`
- `docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md`
- `docs/DISTRIBUTION_OPERATIONS.md`
- `docs/SERVER_CONTROL_AGENT_OPERATIONS.md`
- `docs/ADMIN_CATALOG_OPERATIONS.md`

强制边界：

- 玩家进入所有活动的 Velocity 目标统一为 `activity`，当前物理入口为 owl5 回环
  `127.0.0.1:25568`。
- 占用活动入口的后端统一属于 `owl5-activity-slot`，同一时刻只能运行一个。
- 整合包直接部署可以选择固定 `activity` 或由后台创建的 `activity-*` 动态槽。动态槽
  必须从固定 `activity` 安全模板派生，继续使用 owl5、`127.0.0.1:25568` 和
  `owl5-activity-slot`；创建后默认停止、隐藏，部署整合包前禁止启动。
- 活动企划仍默认绑定固定 `activity`。除非另有经过数据库、准入和双后台同步设计的
  明确任务，不得把动态槽自动纳入企划发布或玩家目录。
- 不得把新活动接到 `survival2`、`lobby`、`pvp` 或其他游戏服目标。
- 不同 Minecraft 版本、加载器或独立模组集合使用不同客户端档案和可写 `.minecraft`；
  同一活动的兼容修复才在原档案上升版。
- 新物理后端必须先填写组件计划，按代理单例、内部大厅、VPS 主机和后端加载器分层
  接入；不得复制大厅、Survival 或旧活动服的完整 `plugins/mods/config` 目录。
- 启动器是唯一换服入口。活动代码不得增加 `/hub`、大厅 NPC、自动回大厅或代理回退。
- 服务端对玩法状态拥有最终权威；客户端数据必须验证、限流并在正确线程应用。
- 部署默认以停服状态结束。没有当前任务中的明确授权，不启动、重启或切换生产后端。

## 交付

- 功能源码、测试、无秘密部署模板和文档形成范围清楚、可独立回滚的 Git 提交。
- 构建产物、运行目录、世界、日志、诊断包和秘密不进入 Git。
- 正式对象、签名清单、发布记录和标签不可覆盖；修复必须使用更高版本。
- 完成声明必须附真实构建/测试、部署后健康检查、回滚路径和未完成的真人验收，不得把
  “命令已发出”当作成功。

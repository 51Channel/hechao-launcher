# ServerControlAgent 0.6.0 候选

- 状态：`NOT_DEPLOYED`
- 日期：2026-08-14
- 目标主机：owl5

## 范围

- 移除单一 `owl5/activity/25568` 的 Agent 配置硬编码；多个目标可通过
  `packageDeploymentEnabled=true` 显式开放整合包部署。
- 保留目标目录边界、受管启动脚本、主机固定路径、世界路径、停服检查、单 Agent 单部署
  互斥、归档摘要、同卷原子切换和失败回滚。
- owl5 候选配置新增 `survival2`：`E:\Survival2`、`25565`、
  `owl5-survival-slot`、`6144 MiB`，保留 `forwarding.secret` 和三个世界目录。
- `activity` 既有能力保持不变；删除能力没有扩大，`survival2` 仍不可从后台永久删除。

## 验证与上线门禁

Agent 配置聚焦测试 `13/13`、完整 Agent `57/57`、完整解决方案 `730/730` 已通过，
Release 构建为 `0` 警告、`0` 错误；生产配置同时识别 `activity` 与 `survival2`。上线前
必须在 owl5 只读检查 `survival2/start.bat` 的 `HECHAO_MANAGED_START` 标记、计划任务
参数、目标停服状态、目录备份和零进行中 `DeployPackage`。安装器任一检查失败必须自动
恢复 `0.5.0` 配置和二进制。

本候选未连接 owl5、未替换配置或 EXE、未重启 Agent，也未启动、停止或重启任何
Minecraft 服务端。

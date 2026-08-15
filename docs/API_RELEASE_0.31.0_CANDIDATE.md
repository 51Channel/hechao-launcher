# Launcher API 0.31.0 多部署槽候选

> 日期：2026-08-15
>
> 状态：候选已验证，尚未发布生产。

## 变更

- 新增 `POST /v1/admin/server-control/deployment-slots`，仅允许完成 MFA 的
  `Administrator` 创建 `activity-*` 动态部署槽。
- 新增迁移 `029_dynamic_deployment_slots.sql`，记录 `Provisioning / Ready / Failed`
  状态、创建人、模板、操作和失败信息。
- 整合包导入页可选择全部已就绪活动部署槽，并可新建动态槽；代理确认成功后自动选中。
- 部署目标仍固定为 `owl5 / 127.0.0.1:25568 / owl5-activity-slot`。管理员不能提交
  任意 VPS 路径、端口、任务名或启动命令。
- 动态槽默认停止、隐藏且不进入玩家目录；活动企划继续默认使用固定 `activity`。

## 安全与回滚

- API 使用串行化事务同时检查服务器目录 ID、服控目标 ID、模板代理新鲜度和数量上限。
- 单代理最多保留 `16` 个 `Provisioning / Ready` 动态槽；`Failed` 记录保留审计但不占
  额度。
- API 只有在心跳确认正确代理、部署能力、端口和冲突组后才把槽标为 `Ready`。
- 生产发布前必须备份 PostgreSQL、API release 和环境文件。迁移 `029` 为加法迁移；
  发生异常时停止新槽创建并恢复 API release，不删除审计或槽状态记录。
- 本版本不启动、停止或重启 Minecraft，也不改变 Velocity 路由和客户端通道。

## 候选验证

- API：`337/337`。
- 完整解决方案：`765/765`。
- Vue/Vite 构建和类型检查：通过。
- Vitest：`13/13`。
- Playwright：`32/32`，包含桌面和 `390px` 新槽流程。
- PowerShell 7：`47` 个脚本解析通过。
- 发布溯源：`20/20`；`git diff --check`：通过。
- owl5 SSH 只读核验：PowerShell `7.6.4`、固定模板文件和主机固定文件存在，动态槽根
  尚未创建；现有活动进程保持原状。

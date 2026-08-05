# API 0.28.0 发布候选

## 功能范围

- 管理后台服控面板新增“删除服务端文件”危险操作区；
- 只有代理心跳显式开放删除且目标已停服时可提交；
- 精确确认文本为 `DELETE <serverId>`，同时要求填写审计原因；
- 新增服务端文件存在状态和暂存清理状态，删除后禁用启动、重启和快捷设置；
- 数据库迁移 `024_server_directory_deletion` 只增加结构化状态列及动作约束，不保存或执行
  任意路径、Shell 或 PowerShell 文本。

## 安全边界

API 不接收删除路径，只把目标 ID 和结构化 `DeleteServerFiles` 命令交给已经绑定该目标的
VPS 代理。目录仍由代理本机白名单配置决定。代理离线、状态过期、目标运行中、存在未完成
命令或删除能力关闭时，API 均失败关闭。

## 候选验证

- API `278/278`；
- 完整解决方案 `666/666`；
- Vue TypeScript 检查通过，Vitest `8/8`；
- Playwright `15/15`，包含删除确认、响应式和 WCAG A/AA；
- Impeccable detector 无问题；
- ServerControlAgent `51/51`。

## 发布与回滚

生产必须先备份 PostgreSQL 和当前 API，再部署 API，最后升级 VPS 代理。API 就绪失败时
立即恢复上一 `current` 链接。迁移 024 是向后兼容加列，但 `0.27.3` 不认识新的动作值；
一旦生产产生 `DeleteServerFiles` 操作记录，不得直接回滚到 `0.27.3`。

候选阶段不得执行真实删除操作，也不得启停 Minecraft 或 Velocity。

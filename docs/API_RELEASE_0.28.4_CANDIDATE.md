# API 0.28.4 发布候选

## 修复范围

- 已删除活动服目录导致代理无法读取 `server.properties` 和 JVM 参数时，不再把
  `settings=null` 解释为整合包部署内存上限 `0 MiB`；
- API 为严格匹配 `owl5` 固定 `activity` 槽、允许整合包部署且目录不存在的目标提供
  受控 `4096 MiB` 部署上限；目录存在但设置读取失败、目标身份不匹配或部署能力关闭时
  继续拒绝；
- 管理后台使用 API 返回的部署上限决定内存输入范围和“发布并部署”按钮状态，不再自行
  从可空的快速设置推导；
- 确认接口和部署编排使用同一规则，避免页面允许提交后后台又以
  `DEPLOYMENT_MEMORY_INVALID` 失败；
- 活动目录不存在时，服务端初始内存使用受控默认值，并继续由 owl5 代理的
  `8192 MiB` 本地硬上限做最终校验。

根因是服务端目录删除后，服控代理仍应保留目标身份、停服状态和部署能力，但无法读取
已经不存在的服务端配置文件，因此按设计上报 `settings=null`。原页面把这个空值退化成
`0 MiB`，导致其他检查全部通过时按钮仍保持禁用；后台编排也只接受实时设置中的硬上限。

## 候选验证

- 规则、后台合同和管理负载聚焦测试 `15/15`；
- API `285/285`、完整解决方案 `.NET 673/673`；
- TypeScript 检查和 Release 全解决方案构建通过；
- Vitest `8/8`、Playwright `16/16`；
- Playwright 使用 `serverFilesPresent=false`、`settings=null` 和 API 派生上限
  `4096 MiB`，确认精确文本输入后按钮可点击并成功提交；
- PowerShell 7 合规、差异敏感信息扫描和 `git diff --check` 通过；
- NuGet 漏洞数据源超时仅产生 `NU1900`，所有项目编译和测试成功，未修改依赖。

## 发布与回滚

本版本没有数据库迁移，不升级 VPS 代理，只发布 API `0.28.4`。部署只允许重启
`hechao-launcher-api.service`，不得操作 Minecraft、Velocity、Publisher 或两台
ServerControlAgent。

生产已经存在 `DeleteServerFiles` 操作记录，不能回滚到 `0.27.3`。本版本的直接安全
回滚目标是 API `0.28.3`；readiness 或后置门禁失败时安装器必须自动恢复该版本。

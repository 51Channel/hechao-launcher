# API 0.32.1 全命令控制台候选

## 范围

- `allowedCommandPrefixes=["*"]` 表示允许全部 Minecraft、模组和插件命令；
- API 心跳校验、排队门禁和 Vue 后台使用相同通配合同；
- `stop`、`restart`、`shutdown`、`end` 始终拒绝自由控制台提交，必须走结构化停止或
  重启按钮；
- 后台明确显示“全部 Minecraft 与插件命令”，不再把 `*` 当作普通前缀展示；
- 生产模板将 owl5、owl9 的全部现有目标改为 `*`，以后独立槽从 owl5 固定
  `activity` 模板继承该策略。

## 版本与顺序

- API 候选：`0.32.1`；
- ServerControlAgent 候选：`0.7.2`；
- 先部署 API 并验证旧代理心跳，再升级 owl5 和 owl9 代理与无秘密配置；
- 代理升级只重启服控代理计划任务，不启动、停止或重启 Minecraft 服务端。

## 当前验证

- API：`350/350`；
- ServerControlAgent：`76/76`；
- 完整解决方案：`790/790`；
- Vue 类型检查、Vitest：`13/13`；
- Playwright：`33/33`；
- PowerShell 7 合规：`47/47`；
- 正式发布已完成，制品、双机备份、生产心跳和后台静态资源结果见
  [`API_RELEASE_0.32.1.md`](API_RELEASE_0.32.1.md)。

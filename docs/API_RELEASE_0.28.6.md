# Launcher API 0.28.6 正式发布

- 制品源码提交：`740a2ed044ab1148608ac38531dbc035c67af3a8`
- 生产目录：`/opt/hechao-launcher-api/releases/0.28.6-20260806T150509Z`
- 数据库迁移：`026_package_publisher_progress.sql`
- 发布范围：整合包客户端发布阶段的真实进度采样、对象数、字节数和 ETA 展示

## 行为

- Publisher 在下载、解压、构建、OSS 对象发布和最终化阶段上报结构化进度；
- 后台在存在有效总量时显示确定型进度条，在总量未知时显示不确定型进度；
- `PublishingObjects` 同时显示对象数和字节数；连续两个有效样本后才计算预计剩余时间；
- 磁盘空间不足时显示“可用空间 / 所需空间”，不伪造百分比或 ETA；
- 进度数据只属于当前 Publisher 租约，过期代理不能覆盖新任务进度。

## 制品与生产验收

| 项目 | 结果 |
| --- | --- |
| 部署归档 SHA-256 | `8C38197EF2053BD465A45EC1AE1036AEB772F12FDA6B38D9D4801522629B4ECA` |
| 生产 `Hechao.Api` | 105,054,148 字节 |
| 生产二进制 SHA-256 | `974A67212B477F0E37CE435CAD6C3369D6C4C5F791BE8A1009924CA1186786C3` |
| API 服务 | `active/running`，`NRestarts=0` |
| 健康检查 | `/healthz` 与 `/readyz` 均通过，数据库 `ready` |
| 前端测试 | Vitest `8/8`，Playwright `17/17` |
| 完整 .NET 回归 | `695/695` |

真实生产任务 `777a31bf-acc9-4754-9f4b-a3a2e5be95f1` 已连续上报
`0/4127` 到全部对象完成；客户端发布只执行一次，最终导入状态为 `Completed`。
该任务的旧 Forge 服务端原包只提供 `START-SERVER.cmd`，不符合现有受管部署契约；
生产验收时已在保留原制品和数据库备份后，为这一任务单独生成 `start.bat` 与
`user_jvm_args.txt`。这不是通用自动转换能力，后续整合包仍须按
[`PACKAGE_IMPORT_OPERATIONS.md`](PACKAGE_IMPORT_OPERATIONS.md) 提供受管启动文件。

结构化证据见
[`evidence/PACKAGE_PUBLISH_PROGRESS_PRODUCTION_2026-08-07.json`](evidence/PACKAGE_PUBLISH_PROGRESS_PRODUCTION_2026-08-07.json)。

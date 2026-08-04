# API 0.27.0 正式发布

- 正式发布 ID：`0.27.0-20260803T174833Z`
- 制品源码提交：`f0616a69e95a6dd6ff172369a4bb8883e4e6ab0b`
- 正式标签：`api-v0.27.0`
- 最终生产复核：2026-08-05 CST

## 发布范围

`0.27.0` 在既有 Vue 管理后台、认证、目录、分发和服控能力上增加完整的整合包导入链：

- 8 MiB 分块上传、暂停续传、取消、安全 ZIP/MRPACK 分析和人工复核；
- 独立 Publisher Agent 任务、租约、心跳和回执；
- API 对签名清单、公钥、档案元数据和对象闭合关系做二次验证；
- 客户端发布只写 `Test`，不触碰 Gray 或 Production；
- 服务端部署只允许 `activity / owl5 / 127.0.0.1:25568 / owl5-activity-slot`；
- 可选目录同步只产生隐藏、`Closed` 的活动记录；
- Vue 管理后台增加第十个“整合包导入”路由。

数据库连续应用迁移 `022` 和 `023`，最终为 `23/23`。API 与服控代理继续双层固定活动
槽，导入流程不会自动停止冲突服、启动 Minecraft 或修改 Velocity 路由。

## 正式制品

| 制品 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.27.0-20260803T174833Z.tar.gz` | `45,936,390` | `4E15B64EA706AA6D3B64BC6AFB1CCA635A205385D1E1CBC3145DC37FCA335C10` |
| `Hechao.Api` | `105,011,140` | `14FC6D22338A368B26556FAF108A814A0B1C3CB20C03791FF9E7356DC7D58AD8` |

生产当前链接为
`/opt/hechao-launcher-api/releases/0.27.0-20260803T174833Z`，远端正式二进制哈希与本机构建
一致。

## 备份与部署

- 发布前数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260803T175449Z.dump`，
  `2,617,754` 字节，SHA-256
  `292A714EBB4BD902AACF279E5550781C80129E975A1CCDB678CFE5049F321605`；旁车校验通过。
- API、Nginx、systemd、数据保护和清单快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.27.0-20260803T174833Z`；
  `SHA256SUMS` 自身 SHA-256 为
  `491B3234EF2580A658ED3E4FC8C37D72AFCD611055E57B4EBB209D01E02E85B0`，
  全部条目复核通过。
- systemd 写路径补齐不可变发布清单目录；既有根级清单权限未放宽。
- 最终 API 进程启动于 2026-08-04 03:32:51 CST。2026-08-05 收口复核时 PID 为
  `3062711`、`NRestarts=0`、`active/running`，只监听回环地址。

## 验证

- API：`268/268`；
- Publisher：`39/39`；
- ServerControlAgent：`46/46`；
- 完整解决方案：`633/633`；
- TypeScript、Vitest `8/8`、Playwright `14/14`；
- 内外网 `/healthz` 为 `ok`、`/readyz` 为 `ready`、数据库为 `ready`；
- 最终 API 进程启动以来错误级 journal 为 `0`。

固定试包 `b4620e53-f125-4749-b220-101d17189cc4` 完成上传、识别、Test-only 发布、停止
活动槽部署和隐藏关闭目录同步。原活动目录恢复后，API 中该任务仍为 `Completed`，
Publisher、owl5 命令和整合包活动队列均为 `0`；活动目标上报离线。

## 回滚

迁移 `022/023` 和部署记录已经产生，不能只把 API 二进制降回 `0.26.2`。正常故障处理
先关闭 `PackageImports__Enabled`、停止 Publisher Agent，并在 `0.27.x` 向前修复；这不
影响已运行的游戏服。只有灾难恢复时，才允许把 `0.26.2` 与同一时点的完整数据库、
环境和清单备份一起恢复。存在待处理 `DeployPackage` 命令时也不能降低 owl5 代理版本。

结构化证据见
[`evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json`](evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json)。

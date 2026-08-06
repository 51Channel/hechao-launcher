# API 0.28.5 正式发布

- 正式发布 ID：`0.28.5-20260806T125215Z`
- 制品源码提交：`fb6b9975d6ecea62533f002ab473e8c66b4e7cad`
- 正式标签：`api-v0.28.5`
- 生产切换时间：2026-08-06 20:52 CST

## 功能范围

- 迁移 `025` 为服控目标保存代理上报的 VPS 物理内存；旧代理未上报时保持空值，
  不猜测容量；
- 整合包后台显示 VPS 总内存、推荐最小内存和推荐最大内存；
- 推荐区间只做提示。低于或高于推荐区间时页面会提醒管理员，但不会禁用“发布并部署”；
- 删除旧的 `4096 MiB` 回退上限和内存上限变化门禁；
- 管理员输入仍须满足 `1-64 GiB`、`256 MiB` 整数步长等结构合法性校验。

owl5 实际上报 `18431 MiB`，生产后台据此给出 `4096-8960 MiB` 推荐区间。推荐最小值
按主机内存八分之一计算并限制在 `4-8 GiB`，推荐最大值按一半计算并限制在
`1-16 GiB`，两者均向下对齐 `256 MiB`。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.28.5-20260806T125215Z.tar.gz` | 45,949,985 字节 | `7348C65B5FED89CCA136E234F1CA1A5DC6EB608B44297A39B2EC8DBB56AEBB55` |
| `Hechao.Api` | 105,038,788 字节 | `D24FDBC352E2485FF8C5992F21CA4074B26E4C77CD4DAFF68EF379D7647F4C22` |
| `chunk-PackageImportsView.js` | 28,488 字节 | `7E5FA0DEF6B2BCC1302A4548409B9D0B5514F8E038877F5B3415BC38A89368E9` |

归档共 158 项、153 个文件，不含 PDB、生产配置或凭据。公网后台分块与本地构建摘要
一致，并返回 `Cache-Control: no-store`。

## 备份与部署

- 数据库备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260806T125340Z.dump`；
- 数据库备份大小 3,647,812 字节，SHA-256：
  `C02D45AF6A65205835D90D9D6026B36B60004C5B6A3E797C4F68628E9F18A858`；
- 配置与旧发布备份：
  `/var/backups/hechao-launcher/releases/0.28.5-20260806T125215Z`；
- 安装器原子切换 `current`，后置门禁失败会恢复 API `0.28.4`；
- 本次只重启 API，没有控制 Publisher、Minecraft、Velocity 或服控代理。

## 验证

- API `287/287`、完整解决方案 `.NET 680/680`；
- TypeScript、Vite 生产构建、Vitest `8/8`、Playwright `16/16` 通过；
- Playwright 验证 `32 GiB` VPS 显示推荐 `4-16 GiB`，填写 `20 GiB` 后仍可提交；
- Impeccable 检测零项；
- 迁移 `25/25`，`host_total_memory_mib` 为可空整数列；
- 服务 `active/running`、PID `1348497`、`NRestarts=0`，只监听
  `127.0.0.1:8090`；
- 回环与公网 `/healthz`、`/readyz` 均返回 `0.28.5` 和 `database=ready`；
- 部署后 warning/error 为 0；Publisher PID `4028581` 未变化；
- 发布/部署队列为 0，服控活动命令为 0，原导入任务仍有 1 个等待管理员复核；
- 本次三个 `/tmp` 上传文件已精确清理，历史发布、备份和日志未清理。

## 回滚

直接回滚目标为：

`/opt/hechao-launcher-api/releases/0.28.4-20260806T002900Z`

迁移 `025` 只增加可空列，回滚二进制时可以保留。生产已有 `DeleteServerFiles` 记录，
不得回滚到无法识别该操作类型的 `0.27.3`。

结构化证据见
[`evidence/PACKAGE_MEMORY_GUIDANCE_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/PACKAGE_MEMORY_GUIDANCE_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

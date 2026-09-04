# 赫朝启动器 API 0.38.1 正式发布

- 发布日期：`2026-09-04`
- 发布目录：`/opt/hechao-launcher-api/releases/0.38.1-20260904T002600Z`
- 制品源码提交：`d7191c777bf572741e4650c4b32a1800347a7a25`
- 正式标签：`api-v0.38.1`
- 数据库迁移：保持 `35/35`，本版不新增迁移

## 问题与修复

管理员已经从赫朝启动器成功打开新的后台标签页时，先前停留在“需要管理员身份”的
深层链接不会感知同源 Cookie 已经建立，因此旧页会一直保持未登录画面。生产只读诊断
确认票据兑换、Web 会话、可信设备、MFA、PostgreSQL、Nginx 和 Cookie 均正常，根因是
旧标签页没有重新检查会话。

本版为登录页增加受控的会话恢复：

- 窗口重新获得焦点、页面重新可见或从浏览器缓存恢复时立即检查；
- 页面可见期间每 `5` 秒低频检查，覆盖并排窗口和焦点事件缺失；
- 检查不闪烁加载页、不覆盖原提示，也不会并发发送重复请求；
- 恢复成功后保留原深层路由和 `server` 查询参数；
- 仍必须由启动器一次性票据建立管理员会话，可信设备只跳过同一管理员的 MFA，
  没有扩大认证边界。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.38.1-20260904T002600Z-linux-x64.tar.gz` | `46,999,944` 字节 | `04E78E5EB7803EF0404C1966F67F0B11983DD85D65C81D8A28193EA1E0560CFA` |
| `Hechao.Api` | `105,703,492` 字节 | `42080526D4B4FA4FA34663A89F3CDB6F6F42DEEED955DCB38E58C82B86E07B2C` |

本地归档、生产可执行文件和不可变发布目录已重新计算摘要。部署前备份为：

- API 配置与当前发布：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.38.1-20260904T002600Z`；
- PostgreSQL：
  `/var/backups/hechao-launcher/database/hechao-launcher-pre-api-0.38.1-20260904T002600Z.dump`，
  `8,001,642` 字节，SHA-256
  `780A6ADDA7CF710BE5CEA30EDC3E7CC9BE19DBA8B8A3C7AF44DB61CABF6CCBBF`。

数据库备份已通过 `pg_restore --list` 可读性检查。

## 生产验收

- AdminWeb Vitest `19/19`、Playwright `35/35`、完整 .NET 解决方案 `838` 项通过，
  `1` 项外部 PostgreSQL 条件测试按环境跳过；
- PowerShell 7 合规 `49/49`、发布溯源 `29/29` 和 `git diff --check` 通过；
- `current` 指向上述不可变发布目录；服务为 `active/running`，主 PID `1521103`，
  `NRestarts=0`；
- 回环和公网 `/healthz`、`/readyz` 均返回 `200`，发布后的 warning 及以上日志为 `0`；
- 数据库保持 `35/35`，整合包、服控操作和服控命令队列均为 `0`；
- `admin.hechao.world/admin/control?server=minigame-commercial-street` 返回 `200`；匿名
  管理员会话返回 `401`，错误 Host 的管理入口返回 `404`；
- Chrome 真实管理员会话从原商业街深层链接恢复到“服控面板”，没有再次停在
  “需要管理员身份”，浏览器 warning/error 为 `0`；
- `api.hechao.world/health` 返回 `200`，`hechao.world` 跳转后的页面返回 `200`。

本次只原子切换并重启 `hechao-launcher-api.service`，没有修改 Nginx、Publisher、
Velocity，也没有启动、停止、重启或发送命令给任何 Minecraft 服务端。

## 回滚

程序故障时原子切回
`/opt/hechao-launcher-api/releases/0.38.0-20260903T084151Z`，只重启
`hechao-launcher-api.service`。本版没有数据库迁移，数据库结构无需回退；回滚前仍应
保留当前数据库快照和审计记录。

结构化证据见
[`evidence/API_0.38.1_PRODUCTION_DEPLOYMENT_2026-09-04.json`](evidence/API_0.38.1_PRODUCTION_DEPLOYMENT_2026-09-04.json)。

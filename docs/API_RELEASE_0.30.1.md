# API 0.30.1 正式发布

- 正式发布 ID：`0.30.1-20260810T225350Z`
- 制品源码提交：`34d2cad65766d0a547bcd611a2a3f55951187897`
- 正式标签：`api-v0.30.1`
- 生产切换时间：2026-08-11 07:00（Asia/Shanghai）
- 数据库迁移：无，保持 `028`

## 修复范围

- 修复生产环境从后台侧栏打开“活动企划”时页面停留在原路由的问题。FullCalendar
  `6.1.21` 会动态创建内联 `<style>`；原 `style-src 'self'` 拒绝该样式表后，库继续读取
  `style.sheet.cssRules`，触发空引用并中断 Vue 路由加载；
- Vue 入口预置内容固定的 `<style data-fullcalendar>` 锚点，生产 CSP 只授权该内容对应的
  SHA-256。FullCalendar 复用已授权 stylesheet 后通过 CSSOM 写入自身规则；
- CSP 保持严格同源并明确不包含 `'unsafe-inline'`。本次不修改企划 API、数据库、权限、
  整合包、活动槽或 Minecraft 控制逻辑。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.1-20260810T225350Z.tar.gz` | 46,244,083 字节 | `9CBDDECEBA9A6C97E76CE348694B18576164921BF48C84369F315B460BA00DE5` |
| `Hechao.Api` | 105,230,788 字节 | `239110B12F8ADE39B32E8669678217F5BB996384A23D457993662545AE98A9F7` |
| `wwwroot/admin/index.html` | 588 字节 | `B405B67805E3E18971B7EE125FE6314C46F53F347DB2EBA9433EF3E08DF59896` |
| `wwwroot/admin/assets/chunk-ActivityPlansView.js` | 251,393 字节 | `738EF480166B5AEBC5D57CDFB95F98013C33BFBB0AFCEADEF075CFCD53AE0E0A` |

归档共 161 项、156 个文件，只包含单文件 API、静态管理后台与端点清单；路径检查、
禁止文件检查和远端哈希复验通过。环境文件、PDB、凭据、Cookie 和签名 URL 未进入制品。

## 测试

- 管理后台 TypeScript 与 Vite 生产构建通过；构建后的入口保留完全一致的样式锚点，
  SHA-256 为 `ipzKv5H4ieKlTTlJ/yUoqe2zh7iU5Iy8a9PrIETK5us=`；
- Vitest `11/11`、Playwright `26/26`。新增回归在生产 CSP 下从
  `/admin/package-imports` 点击进入 `/admin/activity-plans`，确认标题、月历和
  `cssRules` 错误边界；
- API `.NET 303/303`，完整解决方案 `.NET 722/722`；静态托管测试同时验证源码入口、
  构建入口、哈希和 CSP 不含 `'unsafe-inline'`。

## 备份与部署

- PostgreSQL custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260810T225646Z.dump`，
  5,066,519 字节，SHA-256
  `E37AFFF68DB6F14A736CBE599FF47A194CC150746F614D2756BF12C691089319`；同名旁车校验
  与 `pg_restore --list` 均通过，目录记录 239 行；
- API 环境、systemd、Nginx、当前链接、旧二进制哈希与完整 `0.30.0` release 快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.1-20260810T225755Z.tar.gz`，
  46,133,684 字节，SHA-256
  `692B5B29ACFC4F86CDD966E727BDC74DC095FBBF731546568FAD36FC3545F300`；
- 2026-08-11 07:00:16 至 07:00:25 CST 原子切换到
  `/opt/hechao-launcher-api/releases/0.30.1-20260810T225350Z`；只重启
  `hechao-launcher-api.service`。Publisher PID 保持 `2064`、`NRestarts=0`，未操作
  Nginx、Minecraft、Velocity、Publisher 或 ServerControlAgent；上传临时目录已清理。

## 生产验收

- API `active/running`，PID `1756099`、`NRestarts=0`，只监听 `127.0.0.1:8090`；
  回环与公网 `/healthz`、`/readyz` 均返回 `0.30.1` 和 `database=ready`；
- `admin.hechao.world/admin/activity-plans` 返回 `200`，错误 Host
  `launcher-api.hechao.world/admin/activity-plans` 返回 `404`；CSP 含精确样式哈希且
  不含 `'unsafe-inline'`，入口锚点存在，公网日历分块哈希与 release 一致；
- 真实已登录 Edge 会话刷新后，从“整合包导入”点击“活动企划”成功进入目标 URL，标题和
  FullCalendar 可见。部署后浏览器错误为 0；历史 `cssRules` 错误时间均早于部署；
- 匿名目录为 `200`、无效 Bearer 为 `401`，`hechao.world` 与 `api.hechao.world` 为
  `200`，公网 8090 不可连接，部署后 API warning/error 为 0；
- 数据库仍为迁移 `28/28`，企划、已发布企划、活动槽部署身份、进行中服控操作和进行中
  整合包任务均为 0。根盘使用率 `71%`，可用 14,568,697,856 字节。

已经打开修复前后台页面的浏览器标签仍持有旧文档 CSP，需要刷新一次取得新入口和响应头。

## 回滚

直接程序回滚目标为：

`/opt/hechao-launcher-api/releases/0.30.0-20260809T232800Z`

本版本没有数据库迁移或合同变化；回滚只需原子恢复 `current` 并重启 API。数据库备份、
企划、审计、整合包和迁移 `028` 不应删除或恢复。

结构化证据见
[`evidence/API_0.30.1_PRODUCTION_DEPLOYMENT_2026-08-11.json`](evidence/API_0.30.1_PRODUCTION_DEPLOYMENT_2026-08-11.json)。

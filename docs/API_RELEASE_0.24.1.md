# API 0.24.1

- 发布 ID：`0.24.1-20260731T105946Z`
- 正式标签：`api-v0.24.1`
- 功能提交：`91469c8953adc9a0ec384a21c9c16729ab096cae`
- 修复及制品源码提交：`4fdd58ce4fff0fe5f8432214cb77d9d546012586`
- 生产切换时间：2026-07-31 19:01 CST

## 1. 变更

- 服务器目录继续保留 `Online`、`Maintenance`、`Closed` 三种管理员策略。
- 当目录 ID 存在同名服控目标时，玩家目录和管理员后台会结合该物理服的最新运行状态计算实际可用状态。
- `Online` 策略下，物理服停止时自动显示“服务已停止”，服控代理失联时显示“服控失联”，重新运行并恢复心跳后自动开放。
- Velocity 心跳仍负责共享入口状态和在线人数；具体物理服是否运行由 `server_control_targets.server_id` 判断，因此共享 `activity` 入口不再把另一活动误认为当前活动已开启。
- 管理后台服务器目录每 5 秒自动刷新；Velocity 授权在共享目标中优先匹配实际运行的物理服。
- 修复管理员目录联表查询的 PostgreSQL `42702` 歧义列错误，并增加 SQL 契约回归测试。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.24.1-20260731T105946Z.tar.gz` | `45,644,480` | `85E6BC7EE935678BB275A09611FDAF6DF39D38D24C104A7C24D2A10C0B93CAF7` |
| `Hechao.Api` | `104,606,771` | `E5449CA15BE8B60154601EC54C2B0408A9E4D1C1DAB090A4835602CF31CE15DB` |

归档共有 `113` 个条目，包含单文件 API、静态资源端点清单和管理后台资源；不包含 PDB、环境文件或凭据。

## 3. 失败候选

`0.24.1-20260731T103311Z` 在首次生产切换后暴露管理员目录联表歧义，`GET /v1/admin/catalog/servers` 返回 `500`。生产已立即原子回滚到 `0.24.0-20260731T062107Z`，随后由独立提交修复并生成新的发布 ID。

失败归档 `hechao-api-0.24.1-20260731T103311Z.tar.gz` 的 SHA-256 为 `BF2BA9667A1F40A118CF3C2CF5F673392CFAEC03B27106737FBD80FBA7317E6B`。该候选不得复用、不得重新部署，也没有正式标签。

## 4. 验证

- API 测试：`227/227`。
- 当前完整解决方案串行测试：`480/480`。
- 管理后台 JavaScript 语法和 `git diff --check`：通过。
- 生产 `current`：`/opt/hechao-launcher-api/releases/0.24.1-20260731T105946Z`。
- `hechao-launcher-api.service`：`active/running`，`NRestarts=0`。
- 本机和公网 `/healthz`、`/readyz`：`200`，版本 `0.24.1`，数据库 `ready`。
- 发布后错误级日志：`0`。
- 管理后台目录：`6` 条记录；公开目录隐藏内部大厅后为 `5` 条记录。

生产状态校正后：

- `survival1`：策略 `Online`，物理服在线，客户端档案为 `base-1.21.11 v1.0.5`。
- `activity`：策略 `Online`，物理服在线，公开目录自动为 `Online`。
- `pvp`：策略 `Online`，恐怖整蛊物理服停止，公开目录自动为 `Closed`。
- `dollnight`：继续保留显式 `Maintenance` 策略。

详细证据见 [`evidence/CATALOG_SERVER_CONTROL_AVAILABILITY_ACCEPTANCE_2026-07-31.json`](evidence/CATALOG_SERVER_CONTROL_AVAILABILITY_ACCEPTANCE_2026-07-31.json)。

## 5. 回滚与边界

发布使用 [`install-release.sh`](../deploy/linux/install-release.sh) 原子切换并检查 `/readyz`。直接回滚目标为：

`/opt/hechao-launcher-api/releases/0.24.0-20260731T062107Z`

本次只重启 `hechao-launcher-api.service`，没有由部署流程启动、停止或重启任何 Minecraft 服务端；目录数据校正也不会改变游戏服进程。

## 6. 后续服控代理修复

目录状态上线后发现旧版服控代理在执行长停止命令时会暂停同机全部目标心跳。服控代理 `0.2.1` 已将心跳和命令拆为独立循环，并滚动部署到 owl5、owl9；API 版本和目录规则不变。发布与生产证据见 [`SERVER_CONTROL_AGENT_RELEASE_0.2.1.md`](SERVER_CONTROL_AGENT_RELEASE_0.2.1.md)。

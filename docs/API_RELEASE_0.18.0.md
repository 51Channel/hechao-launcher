# API 0.18.0 发布记录

> 状态：已生产部署并完成公网回归
>
> 当前生产：`0.18.0-20260726T234852Z`
>
> 候选发布 ID：`0.18.0-20260726T234852Z`

## 1. 变更

- 新增认证启动器端点 `POST /v1/telemetry/events`。
- 新增管理员汇总端点 `GET /v1/admin/telemetry/summary`。
- 新增管理后台“运行数据”视图和 24 小时、7 天、30 天窗口。
- 新增迁移 15、30 天留存和每 6 小时清理任务。
- 启动器 `0.11.13` 增加隐私受限的离线队列、幂等批量提交和固定失败分类。
- 保留 `0.17.0` 的签名档案三通道、暂停和自动回滚行为。

完整隐私边界与操作说明见
[`LAUNCHER_TELEMETRY_OPERATIONS.md`](LAUNCHER_TELEMETRY_OPERATIONS.md)。

## 2. 候选制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.18.0-20260726T234852Z.tar.gz` | `45,517,797` | `A6873BD6503DD0EAFC8564ED74E77CE46BDEF3B91945C6F1F00198D2EF560167` |
| `Hechao.Api` | - | `ED331D29E066AE1363F4A2E8B1D183272821E1E2E97E0ABC9FF27DA03807EB0F` |

归档没有外部 trust 文件。上传后远端 SHA-256 与本地一致。

## 3. 验收证据

- `.NET 314/314` 通过。
- 管理后台 JavaScript 语法、部署脚本 Bash 语法和 `git diff --check` 通过。
- Chromium 桌面 `1440x1000` 与移动 `390x844` 视图无水平溢出。
- 隔离程序使用生产环境配置副本、生产数据库备份和独立数据库运行。
- 迁移最大值为 15。
- 遥测批次重复提交返回幂等结果，管理员汇总包含对应事件且未重复计数。
- 签名导入、不可变存储、Test/Gray/Production、稳定灰度、暂停自动回滚、
  恢复不自动推广、目录分桶和修订冲突全部通过。
- 临时数据库、目录和 systemd 单元已清理；生产链接仍指向
  `0.17.0-20260726T231515Z`，生产服务 `NRestarts=0`。

## 4. 生产切换

生产切换前统一备份位于：

```text
/var/backups/hechao-unified-account/20260727T000015Z
```

备份清单 SHA-256 为
`068D90C8E21DC4F277E78FA09951C3587F9B7D9C57CBD731E8D23D97A7BC33E6`。
其中 PostgreSQL custom dump 为 `119,611` 字节，SHA-256
`2D85CB21711B8817202A5177FF3BC96E27B7AB2B4540B5ADD9B1FE0530815C75`，
`pg_restore --list` 成功读取 145 个目录项。API 当前发布、环境文件、论坛 SQLite、
论坛源码和环境文件的清单校验也全部通过。

[`install-release.sh`](../deploy/linux/install-release.sh) 原子切换后：

- `current` 指向
  `/opt/hechao-launcher-api/releases/0.18.0-20260726T234852Z`。
- 安装后的 `Hechao.Api` SHA-256 与候选制品一致。
- `/healthz`、`/readyz` 和公网入口均报告 `0.18.0`、database ready。
- 迁移最大值为 15，遥测表存在且初始记录数为 0。
- 目录仍为 6 台服务器、6 个档案、6 个发布和 18 个通道。
- 五个 Velocity 目标心跳继续更新，systemd `NRestarts=0`。
- 部署后 journal 没有 warning/error。
- `hechao.world`、`api.hechao.world` 与管理入口均为 200，
  `launcher-api.hechao.world/admin/` 为 404。
- 未认证遥测和管理员汇总分别返回 401，公网 `8090` 继续超时不可达。

## 5. 回滚

直接回滚目标为 `0.17.0-20260726T231515Z`。迁移 15 为加法变更，回滚时保留遥测表；
旧 API 不读取该表。

# API 0.17.0 发布记录

> 状态：生产部署与公网回归完成
> 候选发布 ID：`0.17.0-20260726T231515Z`
> 日期：`2026-07-27`

## 1. 内容

- 管理后台可创建客户端档案，并原样导入离线签名 JSON 清单。
- API 使用内嵌只读 Ed25519 信任包验签，从签名负载读取全部发布元数据。
- 清单按档案 ID 和原始文件 SHA-256 不可变保存。
- 每个档案固定拥有 Test、Gray、Production 三个发布通道。
- Test 只面向管理员，Gray 面向已登录账号；两者使用账号稳定分桶。
- Production 固定 100%，是匿名和未入灰度账号的正式兜底。
- 支持通道修订冲突、上一版本回滚、发布暂停、暂停自动回滚和恢复不自动推广。
- 管理 Web 增加档案列表、发布抽屉、比例控制、二次确认和审计展示。
- 迁移 14 将旧档案当前版本迁入不可变发布和 Production 指针。
- 旧 `publish-profile.sh` 在迁移 14 存在时拒绝执行。

## 2. 制品

| 项目 | 值 |
| --- | --- |
| Linux 归档 | `artifacts/releases/hechao-api-0.17.0-20260726T231515Z.tar.gz` |
| 归档大小 | `45,493,680` 字节 |
| 归档 SHA-256 | `12AB8A63D95389920255260BED78F12FA1EDD74031ADADCA5CDB1A57B92497C2` |
| `Hechao.Api` SHA-256 | `80CBE367AE39B46B855DAC31A060E6DC7C50FF80135A4040982429068B674C5B` |

发布物是 `linux-x64` 自包含单文件。公钥信任包只作为程序集嵌入资源存在，发布目录
没有可替换的外置信任文件。

## 3. 自动验证

- .NET 全解决方案：`303/303`。
- API：`146/146`。
- 管理脚本 JavaScript 语法、Shell 语法和 `git diff --check` 通过。
- 管理工作台已在 `1440 px` 桌面和 `390 px` 移动宽度完成无横向溢出检查。

## 4. 隔离生产副本演练

候选归档已上传到 API 主机，使用
`/var/backups/hechao-unified-account/20260726T222616Z/launcher-database.dump`
恢复到随机临时 PostgreSQL 数据库，并以随机回环端口和临时 systemd 单元启动。
演练没有修改生产数据库、生产清单目录或生产服务。

真实导入：

- `activity-neoforge-1.21.11` / `1.0.0`
- `activity-neoforge-1.21.11` / `1.0.10`

验证通过：

- 迁移 14 和旧正式发布元数据补全。
- 签名、档案 ID、原始摘要及不可变文件落盘。
- Test、Gray、Production 指派与稳定目录解析。
- 按签名发布时间回滚，而不是按导入顺序回滚。
- 暂停自动回滚 Gray 与 Production。
- 恢复发布不自动重新推广。
- 过期通道修订返回 `409`。
- 发布与通道审计完整。

演练结束后，临时数据库、systemd 单元和测试目录均为 `0`；生产 API 继续运行
`0.16.0-20260726T222124Z`。

## 5. 生产部署

- 功能提交：`4f75838`。
- 部署前统一备份：
  `/var/backups/hechao-unified-account/20260726T232033Z`。
- 备份清单全部通过 `sha256sum -c`，PostgreSQL dump 通过
  `pg_restore --list`。
- `install-release.sh` 原子切换到
  `/opt/hechao-launcher-api/releases/0.17.0-20260726T231515Z`。
- `/healthz` 与 `/readyz` 均报告 `0.17.0` 和数据库 ready。
- 迁移最高版本为 `14`；生产生成 `6` 条发布记录、`18` 条通道记录，
  `6` 个 Production 指针，玩家目录继续返回 `5` 个启用档案。
- 活动档案正式目录仍解析为 `1.0.10` 和清单摘要
  `0e059bbfe9fab6770204de547567ca64420a45e8364fa93206bb316e8ae2b69f`。
- `launcher-api.hechao.world`、`admin.hechao.world`、`hechao.world` 和
  `api.hechao.world` 均返回预期 `200`。
- 心跳在切换后继续返回 `200`；systemd `NRestarts=0`，启动日志无 warning、
  error 或 critical。
- 现场发现的三份旧 `publish-profile.sh` 已先备份到
  `/var/backups/hechao-launcher/legacy-profile-publisher/pre-0.17.0-20260726T232033Z`
  再替换为防绕过版本；在迁移 14 数据库上实测以退出码 `2` 拒绝执行。

本次只重启 `hechao-launcher-api.service`，不启动、停止或重启任何 Minecraft、
Velocity、大厅、生存服或活动服。

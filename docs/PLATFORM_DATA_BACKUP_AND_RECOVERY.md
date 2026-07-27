# 论坛与 Sub2API 备份恢复

> 当前状态：在线一致性本地备份、systemd 沙箱和隔离恢复已验收；加密 OSS 上传、
> 回读与异地主机恢复等待 RAM v5 开放 `backups/services/*`
>
> 更新日期：`2026-07-27`

## 1. 范围

这条备份链覆盖启动器数据库以外、但仍影响赫朝平台恢复的两组数据：

- `hechao.world` 论坛的 SQLite 数据库、源码和受限环境配置。
- `api.hechao.world` 现有 Sub2API 的 PostgreSQL 数据库、Compose 文件和运行配置。

Redis 只保存可重建的临时状态，不作为灾难恢复权威数据。Sub2API 日志、镜像构建目录、
PostgreSQL 数据目录和 Redis 数据目录不会重复打包；数据库使用逻辑转储恢复。

## 2. 一致性与停机边界

论坛数据库通过 SQLite 在线 `.backup` API 生成快照，并对副本执行
`PRAGMA quick_check`。Sub2API 使用容器内 PostgreSQL 16 的 custom-format
`pg_dump`，随后立即执行 `pg_restore --list`。两项操作均不停止服务。

源码和配置在数据库快照后归档：

- 论坛排除 `.git`、`.next`、`.venv`、`venv`、`node_modules`、在线数据库和原始
  `.env`；环境文件以独立的 `0600` 条目写入受限包。
- Sub2API 排除数据库数据目录、Redis 数据目录、构建目录和日志；保留 Compose、
  `.env`、页面和模型配置。

生成过程使用全局 `flock`、一次性 staging、`.partial`、内部 SHA-256 清单、外层
SHA-256 旁车和原子改名。本机只保留最近 7 份，目录和文件均为 root-only。

## 3. 生产组件

```text
/usr/local/sbin/hechao-platform-data-backup
/usr/local/sbin/hechao-offsite-platform-data-backup
/usr/local/sbin/hechao-verify-restored-platform-data
/etc/systemd/system/hechao-offsite-platform-data-backup.service
/etc/systemd/system/hechao-offsite-platform-data-backup.timer
/var/backups/hechao-platform-data/local
/var/backups/hechao-platform-data/staging
/var/backups/hechao-platform-data/offsite-staging
/var/backups/hechao-platform-data/restore-staging
/var/lib/hechao-offsite-platform-backup/latest.json
/var/lib/hechao-offsite-platform-backup/failure.json
```

定时器计划在上海时间每日 `04:20` 运行，并增加最多 20 分钟随机延迟。它与
`03:35` 的启动器数据库异地备份错开。首次 OSS 验收前，定时器保持
`disabled/inactive`。

systemd 服务使用 `ProtectSystem=strict`、只读 Home、私有临时目录和设备、空闲 IO
优先级，并只保留读取受限源配置所需的 `CAP_DAC_READ_SEARCH`。写权限仅开放给该备份
和状态目录；安全暴露评分为 `4.7 OK`。

## 4. 加密与 OSS

本地包使用现有 `Hechao.Backup` 信封加密和数据库恢复公钥加密，再上传到：

```text
backups/services/YYYY/MM/hechao-platform-data-YYYYMMDDTHHMMSSZ.hcbackup
```

发布 RAM 的下一策略版本只新增该前缀的 `oss:GetObject` 和 `oss:PutObject`。不增加
List、Delete、ACL、版本管理或整桶权限。上传后必须立即下载同一对象，验证密文
SHA-256 和逐字节一致，才允许原子写入 `latest.json`。

加密恢复私钥和口令继续沿用已经分离托管的数据库恢复材料；API 主机只保存公钥。

## 5. 恢复验证

解密后的包不得直接覆盖生产目录。验证器按以下顺序工作：

1. 核对外层 SHA-256。
2. 拒绝绝对路径、`..`、符号链接、硬链接和设备条目。
3. 核对内部 `manifest.sha256`。
4. 对论坛 SQLite 副本执行 `PRAGMA quick_check`。
5. 验证论坛源码包和 Sub2API 配置包的安全条目。
6. 把 Sub2API dump 恢复到唯一命名的临时数据库。
7. 核对业务表数量和数据库大小，然后强制删除临时数据库。
8. 无论成功或失败都清理恢复 staging。

生产命令：

```bash
/usr/local/sbin/hechao-verify-restored-platform-data \
  /path/to/hechao-platform-data.tar.gz \
  <expected-plaintext-sha256>
```

## 6. 本地验收证据

2026-07-27 已完成两轮真实在线备份。正式 systemd 沙箱轮次生成：

- 本地包：
  `hechao-platform-data-20260727T133030Z.tar.gz`
- 大小：`35,576,326` 字节
- SHA-256：
  `E3142DD28E58A85B4732C096AF9A2281FC4B9AABB33EA5ED279DBE207EA1D629`
- 论坛 SQLite：`221,184` 字节，`quick_check=ok`
- 隔离恢复的 Sub2API：`77` 张业务表，`179,674,135` 字节
- 恢复后临时数据库：`0`
- 备份和恢复 staging：`0`
- 论坛与 Sub2API 进程启动时间：前后不变

错误 SHA-256 返回非零，且没有留下临时库或 staging。第一版包中发现论坛 `.venv`
符号链接后，安全验证器正确拒绝；最终实现排除虚拟环境而没有放宽提取规则。

机器可读证据见
[`evidence/PLATFORM_DATA_BACKUP_LOCAL_2026-07-27.json`](evidence/PLATFORM_DATA_BACKUP_LOCAL_2026-07-27.json)。

## 7. 尚未完成

以下项目必须在 RAM v5 获得明确确认后执行，完成前不得把本链标记为生产完成：

1. 创建并设为默认策略 v5。
2. 运行首次真实加密 OSS 上传和立即下载复验。
3. 启用并检查 systemd timer。
4. 部署双备份监控并验证告警恢复邮件。
5. 在持有恢复口令的异地主机解密，再回到隔离数据库完成一次真实恢复。
6. 保存非秘密证据并清理所有临时明文。

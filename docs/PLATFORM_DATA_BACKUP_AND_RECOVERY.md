# 论坛与 Sub2API 备份恢复

> 当前状态：RAM v5 最小权限、在线一致性备份、加密 OSS 上传与立即回读、每日
> timer、失败/恢复告警和异地主机隔离恢复均已完成生产验收
>
> 更新日期：`2026-07-28`

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
`03:35` 的启动器数据库异地备份错开。首次 OSS 验收通过后，定时器已设为
`enabled/active`。

systemd 服务使用 `ProtectSystem=strict`、只读 Home、私有临时目录和设备、空闲 IO
优先级，并只保留读取受限源配置所需的 `CAP_DAC_READ_SEARCH`。写权限仅开放给该备份
和状态目录；安全暴露评分为 `4.7 OK`。

## 4. 加密与 OSS

本地包使用现有 `Hechao.Backup` 信封加密和数据库恢复公钥加密，再上传到：

```text
backups/services/YYYY/MM/hechao-platform-data-YYYYMMDDTHHMMSSZ.hcbackup
```

发布 RAM 的 v5 只为该前缀新增 `oss:GetObject` 和 `oss:PutObject`。它不包含
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

## 7. RAM v5 上线前检查

2026-07-27 已在不修改 RAM、OSS、timer 或运行进程的前提下完成：

- 四个 shell 入口全部通过 `bash -n`；
- 线上三个 runner 与 service/timer 文件的 SHA-256 逐个匹配仓库；
- service 为 `inactive`，timer 为 `disabled/inactive`；
- 环境文件为 `0600 root:root`，本地、状态和 staging 目录保持 root-only；
- `systemd-analyze verify` 通过，安全暴露评分仍为 `4.7 OK`；
- 最新本地包旁车校验通过，备份和恢复 staging 均为空；
- 预检时的 v4 对不存在的 `backups/services/*` 对象执行只读 GetObject 探针返回
  `403 AccessDenied`，且没有创建本地输出，证明前缀尚未提前开放；
- 新增策略自动测试精确锁定单个 `Allow` statement、`GetObject/PutObject` 两个动作和
  五个批准前缀；预检证据基线为 `351/351`，当前完整解决方案为 `355/355`。

机器可读证据见
[`evidence/PLATFORM_DATA_BACKUP_RAM_V5_PREFLIGHT_2026-07-27.json`](evidence/PLATFORM_DATA_BACKUP_RAM_V5_PREFLIGHT_2026-07-27.json)。

## 8. RAM v5 生产验收

收到明确确认后，已按预检顺序完成：

1. `HechaoLauncherOssObjectPublish` v5 已设为默认；控制台回读确认只有
   `GetObject/PutObject` 和五个批准前缀。
2. 首份真实对象
   `backups/services/2026/07/hechao-platform-data-20260727T190948Z.hcbackup`
   上传并立即回读，密文 SHA-256 与逐字节比对均通过。
3. owl5 使用受限恢复材料解密同一对象；验证器确认论坛 SQLite 为
   `221,184` 字节、Sub2API 为 `77` 张业务表和 `183,901,207` 字节，临时数据库
   随后删除。
4. `hechao-offsite-platform-data-backup.timer` 已设为 `enabled/active`；验收时
   观察到的下一次运行时间为 `2026-07-28 04:29:40 CST`。
5. 平台监控器升级到 `0.1.2`。受控失败标记产生 Critical 和触发邮件；随后一轮
   真实成功备份自动清除标记，同一告警转为 Resolved 并发送恢复邮件。
6. API、恢复主机的临时明文和一次性任务已清理；管理员中转文件内容已清零。受本机
   工具安全策略限制，三个零字节文件壳和父目录保留，证据中明确记录，没有将其误报
   为已删除。

生产验收使用的恢复源明文 SHA-256 为
`5DF5D5D3A112E637F956880AB37E77FC9A8EBF5469865BA1427ACB4FFC3C5744`。
用于清除演练失败状态的第二轮真实备份完成于 `2026-07-27T19:48:08Z`，service
结果为 `success`、退出码为 `0`。

完整非秘密机器可读证据见
[`evidence/PLATFORM_DATA_BACKUP_RAM_V5_ACCEPTANCE_2026-07-28.json`](evidence/PLATFORM_DATA_BACKUP_RAM_V5_ACCEPTANCE_2026-07-28.json)。

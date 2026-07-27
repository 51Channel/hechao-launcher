# 数据库异地备份与恢复

> 当前状态：RAM v4 最小权限、真实 OSS 上传/下载复验、定时任务、告警恢复、
> 恢复材料回读和异地主机隔离恢复演练均已完成
>
> 更新日期：`2026-07-27`

## 1. 目标与边界

本地每日 dump 不能作为唯一可靠副本。异地流程把 PostgreSQL custom dump 加密后写入
私有 OSS：

```text
backups/database/YYYY/MM/hechao-launcher-YYYYMMDDTHHMMSSZ.hcbackup
```

恢复材料写入单独前缀：

```text
backups/recovery/*
```

Bucket 保持私有并启用阻止公共访问。专用 RAM 只允许上述两个前缀以及既有发布前缀的
`oss:GetObject`、`oss:PutObject`；没有 List、Delete、ACL、版本管理或整桶权限。

## 2. 加密与密钥托管

- 数据：每个备份随机生成 AES-256 密钥，使用 AES-GCM 分块认证加密。
- 密钥包装：4096 位 RSA、OAEP-SHA256。
- 信封：固定 magic、受限 JSON header、每块独立 nonce、关联数据和认证标签。
- 恢复 Key ID：
  `517949CD3B80EB25D46C33A523429C099B809EEC256EB1CE7F240FE1BFE433CD`。
- 公钥进入仓库和 API 主机；加密私钥进入私有 OSS 恢复前缀。
- 私钥口令只保存在游戏 VPS 的受限恢复目录，不进入 API 主机、OSS、Git 或文档。

口令和加密私钥分开保存，任一单点泄露都不能直接解密数据库。文档、日志和收据只保存
对象键、大小、Key ID 与 SHA-256。

## 3. 生产组件

```text
/opt/hechao-backup/Hechao.Backup
/etc/hechao-offsite-backup/database-recovery-public.pem
/etc/hechao-offsite-backup/environment
/usr/local/sbin/hechao-configure-offsite-database-backup-credentials
/usr/local/sbin/hechao-offsite-database-backup
/usr/local/sbin/hechao-verify-restored-database
/var/lib/hechao-offsite-backup/latest.json
/var/lib/hechao-offsite-backup/failure.json
```

systemd timer 每天上海时间 `03:35` 触发，并增加最多 20 分钟随机延迟。服务使用
`flock` 防止并发，低 IO 优先级运行，写权限只开放给备份、临时目录和状态目录。
备份服务只读取权限为 `600 root:root` 的专用环境文件，不读取
`/etc/hechao-launcher-api/environment`。生产核对确认 API 使用只读分发 RAM 身份，
备份服务使用发布 RAM 身份；API 进程不持有备份前缀写权限。

专用凭据从标准输入写入，不把 AccessKey 放入命令行、shell 历史或仓库：

```bash
printf '%s\n%s\n' "$OSS_ACCESS_KEY_ID" "$OSS_ACCESS_KEY_SECRET" |
  /usr/local/sbin/hechao-configure-offsite-database-backup-credentials
```

## 4. 备份流程

1. 调用现有本地备份脚本生成新的 custom dump。
2. 验证同名 SHA-256 和 `pg_restore --list`。
3. 加密到权限 `0700` 的一次性 staging 目录。
4. 使用不可覆盖请求上传，远端已存在时必须长度与 SHA-256 元数据完全相同。
5. 立即下载同一对象，复验 SHA-256 和逐字节一致。
6. 原子写入 `latest.json`，删除旧失败标记。
7. 无论成功或失败都删除 staging；失败原子写入 `failure.json` 并触发 Critical。

手动检查：

```bash
systemctl start hechao-offsite-database-backup.service
systemctl show hechao-offsite-database-backup.service \
  -p Result -p ExecMainStatus
systemctl list-timers hechao-offsite-database-backup.timer --no-pager
jq . /var/lib/hechao-offsite-backup/latest.json
```

## 5. 隔离恢复演练

不得覆盖生产数据库。恢复演练流程：

1. 从收据取得对象键和 SHA-256。
2. 下载到 root-only 临时目录并校验密文 SHA-256。
3. 在持有口令与加密私钥的受控环境解密。
4. 把明文 dump 临时传到 API 主机。
5. 运行：

```bash
/usr/local/sbin/hechao-verify-restored-database \
  /root/recovery-staging/database.dump \
  <expected-plaintext-sha256>
```

脚本创建唯一的 `hechao_offsite_restore_*` 隔离数据库，恢复后核对迁移最大值、档案数、
服务器数、用户数、告警数和数据库大小，并在退出 trap 中删除隔离数据库。它不会连接
或覆盖 `hechao_launcher` 生产库。

6. 保存非秘密 JSON 证据，删除 API 主机和恢复端的明文 dump。

## 6. 首次生产验收

加密工具 `0.1.0`、生产公钥、runner、service、timer、恢复校验脚本和统一告警已部署。
RAM 策略 `HechaoLauncherOssObjectPublish` 的 v4 于 `2026-07-27T12:38:13Z`
创建并设为默认版本。控制台二次查询确认它只允许对 `objects/*`、
`releases/launcher/*`、`backups/database/*` 和 `backups/recovery/*` 执行
`oss:GetObject` 与 `oss:PutObject`，没有 List、Delete、ACL、版本管理或整桶权限。

2026-07-27 已先完成不依赖 OSS 权限的离线恢复预演：API 主机生成新的
PostgreSQL custom dump，在主机上用生产恢复公钥加密，只把 `182,877` 字节密文传到
离机恢复端；恢复端使用加密私钥和独立口令解出 `181,706` 字节明文，明文 SHA-256
`37C1AA13B496576A7B9DFAF98462AB7744C08CA5CCA9088974B439063BB224C9`
与源 dump 一致。明文回传后，隔离恢复器验证迁移版本 `17`、客户端档案 `6`、
服务器 `6`、用户 `22`、告警 `6` 和数据库大小 `12,336,151` 字节，并确认一次性
数据库已删除。API 主机与恢复端的临时明文随后均已清理。该预演验证了
“生成、加密、离机解密、隔离恢复”链。

随后完成真实 OSS 闭环：

- 首份对象为
  `backups/database/2026/07/hechao-launcher-20260727T125652Z.hcbackup`，
  密文 `193,395` 字节，SHA-256
  `3A336B50CE0A505E4CE3802385926B8C4CB17B0CB0AC97A3B2A0BCB4921CB8E2`；
  明文 `192,264` 字节，SHA-256
  `BA32C3FBDCD4430B804CB573D3FB1537AB7B47F715161914CA5094BF56319F59`。
  上传后立即下载并完成 SHA-256 与逐字节比对。
- `hechao-offsite-database-backup.timer` 已启用；首次观察到的下一次执行时间为
  `2026-07-28 03:40:39 CST`。失败标记已清除，平台监控器记录一次恢复转换并成功
  投递恢复邮件。
- 加密数据库恢复私钥已写入
  `backups/recovery/database-backup-v1/database-recovery-private.p8`，生产签名恢复包
  已写入
  `backups/recovery/signing-key-v1/distribution-signing-private.hcbackup`；两者均从
  OSS 回读并与本地密文逐字节一致。
- 同一 OSS 数据库密文在游戏 VPS 使用独立口令解密后，隔离恢复器验证迁移版本
  `17`、客户端档案 `6`、服务器 `6`、用户 `22`、告警 `6` 和数据库大小
  `12,418,071` 字节。唯一命名的恢复数据库随后自动删除，剩余数量为 `0`。
- API 主机、游戏 VPS 和管理员电脑上的本次临时明文及工具目录均已清理。游戏 VPS
  的正式口令副本只允许 `SYSTEM` 与 `Administrator` 完全控制；确认后已删除管理员
  电脑上的临时口令文件。

非秘密机器可读证据见
[`evidence/OFFSITE_BACKUP_RECOVERY_2026-07-27.json`](evidence/OFFSITE_BACKUP_RECOVERY_2026-07-27.json)。

# NeoForge 活动档案 1.0.10 发布记录

> 发布日期：`2026-07-24`
> 档案 ID：`activity-neoforge-1.21.11`
> 生产状态：已发布，活动服务器保持 `Closed 0/30`

## 发布物

- 版本：`1.0.10`
- Minecraft：`1.21.11`
- NeoForge：`21.11.42`
- Java：`21`
- 文件与对象：`4,754`
- 逻辑大小：`621,732,083` 字节
- 清单大小：`2,098,066` 字节
- 清单 SHA-256：`0E059BBFE9FAB6770204DE547567CA64420A45E8364FA93206BB316E8AE2B69F`
- 签名 Key ID：`release-2026-07-primary`
- Meccha SHA-256：`C72511BEF3B0CC2C1A1C97E1C33709901714460191F9549FD461E71215534E9E`

干净源位于 `artifacts/client-sources/activity-neoforge-1.21.11-1.0.10`，发布物位于 `artifacts/distributions/activity-neoforge-1.21.11-1.0.10`。原客户端 `H:\MC\画画躲猫猫` 未修改。发布前完成生产信任验签、发布物闭合验收、全量安装、逐文件复验和不启动游戏的 NeoForge 进程构建。

## 恢复点

- 快照目录：`/var/backups/hechao-launcher/profile-publications/pre-activity-neoforge-1.0.10-20260724T120517Z`
- 数据库备份：`/var/backups/hechao-launcher/database/hechao-launcher-20260724T120517Z.dump`
- 数据库 SHA-256：`5CDF0991013A99A74622BFF23C37C9EC9C999418BB023306F18C33F9987F74A8`
- 清单归档 SHA-256：`5C918781D08434FC581E0F69E91ABF08F5A2E3F2756F3FC985606D51F45F9ACE`
- 发布前档案行：`1.0.9`、`132,120,576` 字节、空 SHA-256
- 发布前活动清单：不存在

数据库校验和及 `pg_restore --list` 均通过。发布脚本先校验远端清单哈希，再以 `root:hechao-api 0640` 原子安装文件并更新数据库；API 没有重启。

## OSS 上传

发布器对 `4,754` 个对象重新计算 SHA-256，发送 Content-MD5，并由 OSS 校验上传：

- OSS 报告上传：`4,754`
- OSS 报告已存在：`0`
- 提交字节：`621,732,083`
- 与基础档案共享：`4,551` 个摘要
- 真正新增：`203` 个摘要、`152,843,997` 字节

Bucket 已开启版本控制。[阿里云 PutObject 文档](https://help.aliyun.com/en/oss/developer-reference/putobject)说明版本控制已开启或暂停时会忽略 `x-oss-forbid-overwrite`，因此共享键被写成同内容的新版本，而不是返回 `FileAlreadyExists`。内容寻址路径、上传前 SHA-256 和 OSS Content-MD5 均通过，未发生内容损坏；但产生了额外版本存储。下一次档案上传前必须增加版本感知的远端元数据检查，或配置 Bucket 级文件覆盖保护。

## 上传保护补强

`2026-07-24` 将发布 RAM 策略 `HechaoLauncherOssObjectPublish` 升级为 v2，仅在 `hechaoworld/objects/*` 上增加 `oss:GetObject`；仍无列举、删除、其他前缀读取或版本管理权限。发布器 `0.7.0` 会在上传前使用 `HeadObject` 校验 `Content-Length` 与 `x-oss-meta-sha256`，匹配则跳过，不匹配则硬失败，仅远端不存在时上传并在上传后再次校验。

使用本档案对生产 OSS 全量复验：`4,754` 个对象全部校验后跳过，上传 `0` 个对象、`0` 字节，没有创建新对象版本。当前解决方案测试为 `154/154`。

## 生产验收

- 公网目录：`1.0.10`、`621,732,083` 字节和目标清单 SHA-256 完全一致。
- 服务器目录：NeoForge，`Closed 0/30`，未启动 Minecraft 或 Velocity。
- 权限：一次性 Member 账号获取活动清单返回 404；临时 Participant 权限可取得清单。
- 清单：从公网取得后通过生产公钥验签。
- 对象：全部 `203` 个新增对象和 `12` 个共享对象样本从 `download.hechao.world` 下载并重算 SHA-256，共校验 `153,102,875` 字节。
- Meccha：生产清单仅包含一份目标 JAR，SHA-256 与活动服一致。
- 清理：隔离用户、活动会话及对应审计记录均为 `0`。
- 回归：`hechao.world`、`api.hechao.world`、公网健康与就绪检查均为 200；管理入口仍为 404。
- 日志：发布与下载验收期间 API 新增 warning/error 为 `0`。

对象下载使用启动器 API 的同源 Bearer 授权，302 后由无 Authorization 的独立客户端请求 `download.hechao.world`，验证了令牌不会跨域发送。

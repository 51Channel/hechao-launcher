# 赫朝平台无密码资产清单

> 核查时间：2026-07-30（Asia/Shanghai；历史容量数值保留各自采样日期）
> 用途：`PLATFORM_PLAN.md` 阶段 0 基线、部署前检查与回滚定位
> 安全边界：本文不记录密码、私钥、数据库口令、令牌或云 AccessKey

## 1. 阿里云分发与网站主机

| 项目 | 当前值 | 证据状态 |
| --- | --- | --- |
| 公网地址 | `8.148.207.171` | 已从 DNS 与主机双向核对 |
| 系统 | Ubuntu 24.04.4 LTS，x86-64，KVM | 已实时读取 |
| CPU | 2 vCPU，Intel Xeon Platinum | 已实时读取 |
| 内存 | `3,669,319,680` 字节可见，核查时可用 `2,331,938,816` 字节；2 GiB Swap | 2026-07-27 实时读取 |
| 系统盘 | `52,448,063,488` 字节，剩余 `34,990,034,944` 字节 | 2026-07-27 实时读取 |
| 入站防火墙 | UFW 仅允许 TCP `22`、`80`、`443` | 已实时读取 |
| 公网带宽 | 200 Mbps 峰值 | 已由同一公网 IP 的阿里云轻量应用服务器控制台截图核对；峰值不等于业务承诺带宽 |

### 1.1 运行服务与监听

| 监听 | 服务 | 用途 |
| --- | --- | --- |
| `0.0.0.0:80/443` | Nginx | 唯一公网 HTTP/HTTPS 入口 |
| `127.0.0.1:3000` | `hechao.service` | `hechao.world` Next.js 网站 |
| `127.0.0.1:8080` | Docker `sub2api` | `api.hechao.world` 中转站 |
| Docker 内网 `5432` | PostgreSQL 16 | Sub2API 数据库，不对公网开放 |
| Docker 内网 `6379` | Redis 7 | Sub2API 缓存，不对公网开放 |
| `0.0.0.0:22` | OpenSSH | 运维入口 |
| `127.0.0.1:5433` | Docker `hechao-launcher-postgres` | 启动器独立 PostgreSQL 16，512 MiB 上限 |
| `127.0.0.1:8090` | `hechao-launcher-api.service` | 启动器 API `0.24.0`、赫朝账号、账号安全、基础设施角色、论坛会话联动、受控全局等级、六份生产档案、诊断上传、客户端兼容保护、运行遥测、服务器深度指标、统一告警与服控内存管理 |

Nginx 当前将 `hechao.world` 根路径转发到 `127.0.0.1:3000`，并保留若干中转 API 路径到 `127.0.0.1:8080`；`api.hechao.world` 全站转发到 `127.0.0.1:8080`。新启动器 API 不得占用这两个现有上游端口或覆盖现有 server block。

启动器 API 自迁移 17 生效至 2026-07-27 23:26（Asia/Shanghai）的 `784` 个分钟桶
共记录 `10,967` 次请求，峰值 `27` 次/分钟（约 `0.45 QPS`），平均耗时
`5.248 ms`、单请求最大耗时 `389 ms`、服务端错误 `0`。这是当前低负载基线，
不替代 20 至 30 人活动窗口的容量测试。

Nginx 隐私日志启用后的 2026-07-27 23:09:42 至 23:41:46（Asia/Shanghai），
`api.hechao.world` 共记录 `269` 次真实请求，全部返回 `200`；峰值为 `16`
次/分钟（约 `0.267 QPS`），累计响应体 `31,696,165` 字节，单分钟响应体峰值
`2,313,574` 字节（约 `0.308 Mbps`）。平均请求时长 `17.197353` 秒、最大
`88.274` 秒；主要 `/responses` 请求会持续流式传输模型输出，因此这里的时长是
端到端流生命周期，不能当作普通接口计算延迟。采样时主机可用内存
`2,402,623,488` 字节、Swap 使用量为 `0`，启动器 API 峰值内存
`122,191,872` 字节且重启数为 `0`，Sub2API 与其 PostgreSQL 分别约占
`139.7 MiB` 与 `203.5 MiB`。

### 1.2 域名与证书基线

| 域名 | 当前状态 |
| --- | --- |
| `hechao.world` | A -> `8.148.207.171`，TTL 约 600 秒，HTTPS 200 |
| `api.hechao.world` | A -> `8.148.207.171`，TTL 约 600 秒，HTTPS 200 |
| `launcher-api.hechao.world` | A -> `8.148.207.171`，HTTPS `/healthz` 与 `/readyz` 均为 200 |
| `admin.hechao.world` | A -> `8.148.207.171`，TLS 有效；管理员 Web 返回 200，启动器 API 域名下的 `/admin/` 保持 404 |
| `download.hechao.world` | CNAME -> `hechaoworld.cn-shanghai.taihangtop.cn`，DigiCert HTTPS 有效；私有 Bucket 根路径按预期返回 403 |

旧站证书由 ZeroSSL 签发，有效期到 2026-10-19，SAN 包含 `hechao.world`、`www.hechao.world` 和 `api.hechao.world`。新证书同样由 ZeroSSL 签发，有效期到 2026-10-19，SAN 包含 `launcher-api.hechao.world` 和 `admin.hechao.world`。`download.hechao.world` 使用独立部署到 OSS 的 DigiCert 证书，有效期到 2026-10-20。

### 1.3 备份与回滚

- 当前保留 `/root/hechao-bootstrap-20260721.tar.gz`，大小约 271 MiB。
- 归档包含重装前的网站源文件、SQLite、Sub2API PostgreSQL dump、Redis RDB、Docker 镜像、系统配置和 SHA-256 清单。
- 归档内记录的 SQLite 完整性为 `ok`，Redis RDB checksum 为 `OK`，PostgreSQL 归档条目数为 982。
- HTTPS 施工前 Nginx、旧证书续期元数据和 root crontab 已备份到 `/var/backups/hechao-launcher/nginx-pre-launcher-https-20260721T155728Z.tar.gz`，权限 `600`，SHA-256 为 `12ee968cba92e3c12ad70280ef6cd7bf604ecf2a4391cd485becb9c7b44efd1f`。
- 目录数据库施工前 API 与 Nginx 已备份到 `/var/backups/hechao-launcher/pre-catalog-stage-20260721T162344Z.tar.gz`，权限 `600`，SHA-256 为 `3a0d04967a13e810bac17ee9eba9d06e0dcb95fe5b0aa227f09832b5ff82d12f`。
- 认证发布前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260721T171655Z.dump` 及其 SHA-256 已校验通过。
- API `0.4.0` 发布前环境、systemd 单元、当前链接和清单目录已备份到 `/var/backups/hechao-launcher/pre-api-0.4.0-20260723T093739Z.tar.gz`，权限 `600`，SHA-256 为 `8fd777e035216be9047c1f8641e64acb2be6551784d8bbf67b76ab04ac597162`。
- API `0.5.0` 发布前数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T102842Z.dump`，SHA-256 为 `f6455e523cebc2ca6ca98d3b0c3ab7eebe4e87489141f3ae4dcf954191e12efc`；配置备份为 `/var/backups/hechao-launcher/api-configuration/environment-before-velocity-20260723T103150Z`。
- API `0.6.0` 部署后数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T124326Z.dump`，SHA-256 为 `508b37c7a695413e2a3d3d5b7ff08212f720077121bb7237c522957ec08d9464`；校验和与 `pg_restore --list` 均通过。
- API `0.9.0` 发布前数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260723T195226Z.dump`，大小 `48,720` 字节，SHA-256 为 `621638f3500680e7ad3903cab62ac40a974defe0ecb65a4eb9cfc292cd5547d6`；校验和与 `pg_restore --list` 均通过。
- API `0.10.0` 发布前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T101600Z.dump` 的 SHA-256 为 `9ceaaea545525e1a6ec199d11aa62fecad4e62220641cc847da2a7d1bb3f64f8`；配置与当前发布备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.10.0-20260724T101600Z.tar.gz` 的 SHA-256 为 `1f7935395a99f85355acde0d7110205ca2a560d8989d9a560b33e8561b0886ba`。
- API `0.10.1` 热修复前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T102852Z.dump` 的 SHA-256 为 `d15397bfb1c318f4141ce97a13ac2a4692c755915ff46bdd9c46c5c6b051d1d4`；配置与当前发布备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.10.1-20260724T102852Z.tar.gz` 的 SHA-256 为 `c5fb969a7a24ebcb69e90f19bf112faf477d2a1ab68c53b7aeac3dc589f90ce4`。两轮数据库备份均通过校验和与 `pg_restore --list`。
- API `0.11.1` 最终限流热修复前发布与配置备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.11.1-hotfix-20260725T165050Z.tar.gz` 为 `45,447,113` 字节，SHA-256 为 `52da8d1d120d2f3a5983128b66cf22f69c1ebe56754527b04a350cace8bbecb4`；直接回滚目标为 `0.11.1-20260725T160210Z`。
- API `0.11.1` 部署一致性数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260725T170025Z.dump` 为 `80,137` 字节，SHA-256 为 `0e32ffddf4aaa0c0306a2950ae2eee9990921aa26dc87a63eedd256cc6f0b208`；校验和通过，`pg_restore --list` 可读取 `110` 条归档目录项。
- API `0.12.0` 部署前发布与配置备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.11.3-20260725T203220Z.tar.gz` 的 SHA-256 为 `71d850aabd85ab203ce585c679a53609f91f013dfdae6937e1b208e88625ec12`；数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260725T203227Z.dump` 的 SHA-256 为 `e1e3f1f864d1cb363e426346892dc0c6651409e001da9f0b05f9435d55a5c7d9`。该次发布为 `0.12.0-20260725T203001Z`，当时直接回滚目标为 `0.11.3-20260725T195000Z`。
- API `0.13.0` 部署前完整备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.13.0-full-20260726T173217Z.tar.gz` 的 SHA-256 为 `8868A57C5482C47406AC83F2B847FF8389ECB7B64FFF5B00B86C33D66846C23D`；数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260726T173201Z.dump` 的 SHA-256 为 `DAEB5CE12E3ED23561A734FB8A228598DCA20F117A450E4CBB2D4029016EB14C`。该次发布为 `0.13.0-20260726T173536Z`，当时直接回滚目标为 `0.12.0-20260725T203001Z`。
- API `0.14.1` 发布前、迁移 10 已应用后的数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260726T191147Z.dump` 为 `94,908` 字节，SHA-256 为 `c2d9563544bffdf4060bc51ff93a5c27d1d13c84c1d25f6ec3c963aaa7181029`；当前 API、环境、systemd 与 Nginx 完整备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.14.1-20260726T191147Z.tar.gz` 为 `45,475,987` 字节，SHA-256 为 `e2420923ac01f2bae6e7a81c3eda56370fb7c50e8afa8e4ab4eabcc1e6f669b7`。两份备份均已校验，数据库归档可由 `pg_restore --list` 读取。
- API `0.15.0` 发布前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260726T202823Z.dump` 为 `95,200` 字节，SHA-256 为 `54a9f6c6321bc7adf10ac516e8a634c3c79724382f3d790c72d005fce142721e`；API、环境、systemd 与 Nginx 完整备份 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.15.0-20260726T200900Z-full.tar.gz` 为 `45,500,274` 字节，SHA-256 为 `58ecffe9977c75b3e1e8c068d82048e4585b98bd53f3dd82d6895cba215c6fa7`。两份备份均有 `.sha256`，数据库归档已通过 `pg_restore --list`；同目录早期 `200820Z` 小归档只含符号链接，不作为恢复依据。
- API `0.16.0` 部署前一致性备份目录为 `/var/backups/hechao-unified-account/20260726T222616Z`；`launcher-database.dump` 为 `108,668` 字节、SHA-256 `00B4AEB14F49B596A41311FCAB89B49DB317280B55A2C7AFA1E691658D325784`，`api-current-release.tar.gz` 为 `45,546,432` 字节、SHA-256 `0A8506E5B12156821D50850916025DA83922C34B606B324521D3A724ABB50752`，`forum.sqlite` 和论坛源码也包含在同一八项校验清单中；全部校验通过。
- API `0.19.0` 最终切换前一致性备份目录为 `/var/backups/hechao-unified-account/20260727T005113Z`；`launcher-database.dump` 为 `134,765` 字节、SHA-256 `38C64044B18A77642F76FA534C748459D29989EB38EE18D784028D14B59827C3`，整份八项清单 SHA-256 为 `951BCDCE013EF6F64671AC1113D348474A43A0C5FBF3616DF122161DCC724F31`，`pg_restore --list` 可读取 152 个目录项。
- API `0.20.0` 最终切换前一致性备份目录为 `/var/backups/hechao-unified-account/20260727T021850Z`；数据库 dump SHA-256 为 `A804B8C8B24377FD5B0E5E13D70463691B1A4C42D0B0D303E070B46ED37F5D07`，清单文件 SHA-256 为 `116F86AB1DA4D2C65D92DECE3E684C8FB23F3E816095AABD3FB471AA1927AFDC`，`pg_restore --list` 可读取 167 个目录项。
- API `0.20.1` 切换前一致性备份目录为 `/var/backups/hechao-unified-account/20260727T145731Z`；数据库 dump SHA-256 为 `D148A14E6108B7D800557CF73FC05D8BC5D4F8F2F5B34B01AEB7E6AC358B41CB`，清单文件 SHA-256 为 `A95A941E7EA3E9F8C1C42E5004C47CED4515A3ED86B856F22662179693B1935D`，`pg_restore --list` 可读取 177 个目录项。
- API `0.20.2` 切换前一致性备份目录为 `/var/backups/hechao-unified-account/20260727T230119Z`；数据库 dump SHA-256 为 `025061B836A02983FF0C376CD0D51A0760217B387F4E1ADB728864DDD2C6A6D8`，清单 SHA-256 为 `2C9FDD49DCF30A0AE4C30AC770E6A2DFB928E0B87D14964522C335A12DB1024D`，`pg_restore --list` 可读取 177 个目录项。
- Nginx 日志脱敏切换前配置位于 `/var/backups/hechao-nginx-privacy/20260727T150915Z`；五个 server block 已使用不含查询字符串与 Referer 的 `hechao_privacy` 格式，配置检查和平滑 reload 通过。
- 活动档案 `1.0.10` 发布前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T120517Z.dump` 为 `64,196` 字节，SHA-256 为 `5CDF0991013A99A74622BFF23C37C9EC9C999418BB023306F18C33F9987F74A8`；发布快照目录为 `/var/backups/hechao-launcher/profile-publications/pre-activity-neoforge-1.0.10-20260724T120517Z`，其中清单归档 SHA-256 为 `5C918781D08434FC581E0F69E91ABF08F5A2E3F2756F3FC985606D51F45F9ACE`。数据库校验和与 `pg_restore --list` 均通过。
- 恐怖整蛊档案 `1.0.0`（历史 ID `pvp-fabric-1.20.1`）发布前数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260725T202241Z.dump`；发布快照目录为 `/var/backups/hechao-launcher/profile-publications/pre-pvp-fabric-1.0.0-20260725T202252Z`，清单归档 SHA-256 为 `cd99bb5059b58ea834b0bff8d3a27d061c32439ed5e7d9e079eca21dc4cbcf0f`。
- Vanilla、Forge 与 DollNight 发布前统一备份目录为 `/var/backups/hechao-launcher/profile-publication/20260726T182024Z`；数据库备份 SHA-256 为 `183CA211FA431656FCD982305ACEA1C7859579D6D0BA9DB2511F947C37117334`，清单归档 SHA-256 为 `3A5380329322D61BAC16E8FBCB84C6036D90E44E5399A95DEA3B077AA613D884`。校验和与 `pg_restore --list` 均通过。
- 启动器 `0.11.6` 小范围测试前数据库基线 `/var/backups/hechao-launcher/database/hechao-launcher-20260726T084308Z.dump` 为 `85,620` 字节，SHA-256 为 `199e8811da08e9f9c2f1db88866f9dd51574ab9b043b6ba147b3092ad0413c36`；备份服务结果、校验和与 `pg_restore --list` 均通过。
- `hechao-launcher-db-backup.timer` 已启用，每日生成 PostgreSQL custom-format 备份并保留 14 天；首份备份校验通过，`pg_restore --list` 可读取 36 个对象。
- 首份备份已恢复到唯一命名的临时验证库，迁移版本、3 个客户端档案、4 个服务器和 0 个初始用户均与生产库一致，验证后只删除了临时库。
- 启动器数据库首份真实 OSS 异地备份为
  `backups/database/2026/07/hechao-launcher-20260727T125652Z.hcbackup`，密文
  `193,395` 字节、SHA-256
  `3A336B50CE0A505E4CE3802385926B8C4CB17B0CB0AC97A3B2A0BCB4921CB8E2`；
  上传、立即下载、逐字节复验、异地主机解密和隔离恢复均通过。
- `hechao-offsite-database-backup.timer` 已启用，失败标记已清除，平台监控器已成功
  投递恢复通知。
- 论坛与 Sub2API 在线一致性备份已部署：论坛使用 SQLite 在线快照，Sub2API 使用
  PostgreSQL custom dump，源码、Compose 和运行配置进入同一 root-only 包。本地
  systemd 沙箱生成 `35,576,326` 字节包，隔离恢复出 `77` 张 Sub2API 业务表并自动
  删除临时库。RAM v5 已默认生效；真实加密 OSS 上传与立即回读、owl5 解密、
  `77` 表隔离恢复、每日 timer 及平台监控器 `0.1.2` 的失败/恢复邮件均已通过。
- 阿里云控制台快照状态尚未通过 API 或控制台复核。

### 1.4 客户端签名与发布资产

- 生产签名 Key ID 为 `release-2026-07-primary`，算法为 `ECDSA_P256_SHA256`，公钥 SHA-256 为 `6D4ACA1E787CFEDA1C3A5D7B772FB1F0E03C298848538D272B12BCFAF1C94F9E`。
- 私钥主副本由 Windows DPAPI `CurrentUser` 加密保存在 `%LocalAppData%\HechaoLauncherAdmin\secrets`，同一密文镜像位于 `H:\Hechao-SecureBackup`；两处 ACL 均仅允许当前管理员与 `SYSTEM`。
- 私钥未进入源码、发布包、API 或游戏 VPS；临时 PEM 与恢复演练的二进制 PKCS#8 已清理。发布器 `0.9.0` 已把生产私钥导出为 RSA/AES-GCM 加密恢复包并完成解密、临时 DPAPI 恢复、真实清单签名和生产公钥验签。加密恢复包已写入 `backups/recovery/signing-key-v1/distribution-signing-private.hcbackup` 并完成 OSS 回读逐字节复验；恢复口令副本与密文分离保存在游戏 VPS。
- `0.6.0` 启动器已嵌入该公钥并完成程序集资源加载、真实清单签名/验签、篡改拒绝、全量安装、受管 Java 21、Fabric 进程构建和启动前授权验证；加入状态采集后解决方案测试为 `80/80` 通过。
- `Hechao-Launcher-0.6.0-win-x64.zip` SHA-256 为 `9529C175A168EDE850D4A519E50EA71268BB8A809D128FC5076F18D48D90CC0C`，其中单文件 EXE SHA-256 为 `0DF28FD71DA34303C1FAAC11C1D041884C4AF664D192D3D2A719FAF9A602C2E7`；历史发布器 `Hechao-Publisher-0.5.0-win-x64.zip` SHA-256 为 `176EAF4B50C36A9254E90C8B3EB5F35FAC4089095C594B3A94932B395F46B696`。
- 发布器 `0.6.0` 最终本机候选基于提交 `12432fb5772f365d79784eb141e1af104d20022c`。单文件 EXE 为 `74,018,082` 字节，SHA-256 为 `54B7BE1AD936D2420F54C7164973321B5C9BC59CA30C8C1CA8B9E5284FBF8303`；`Hechao-Publisher-0.6.0-win-x64.zip` 为 `32,088,917` 字节，SHA-256 为 `4B783D51C7F3DA8D71A98DE593042435029AE4F201C48DF3F57CA88F78D31DBF`，归档仅含该 EXE。EXE 为 `NotSigned`，从归档重新解压后已使用生产信任包完整验收活动档案 `1.0.10` 的 `4,754` 个对象。
- 发布器 `0.7.0` 正式候选基于提交 `ac7bc8045c4c5f0b10b84987b8a8cb6f02bb3fca`。单文件 EXE 为 `74,022,178` 字节，SHA-256 为 `78C190972D00C40A1066A6ACB21BE1624E2AF7D08F2FB128D9768E662FEC7BAC`；`Hechao-Publisher-0.7.0-win-x64.zip` 为 `32,090,108` 字节，SHA-256 为 `E05B589976D033015D1FC05D276FE4E19694B9BD7A359569A1AE0473AF1F2F18`，归档仅含该 EXE。产品版本内嵌完整源码提交，EXE 为 `NotSigned`；归档解压哈希与原件一致，程序版本为 `0.7.0`。使用最终 EXE 对生产活动档案复验为 `0` 上传、`4,754` 跳过、`0` 上传字节。完整记录见 [`PUBLISHER_RELEASE_0.7.0.md`](PUBLISHER_RELEASE_0.7.0.md)。
- 启动器 `0.8.0` 最终本机候选为 `68,511,931` 字节，EXE SHA-256 为 `CB21C2A860DFDE961C495281BBD58AAC62ECB064C8E3A6B7B098F7EAF7DB54EC`；它已完成五工作区与短屏响应式检查，但尚未上传或向玩家分发。
- 启动器 `0.8.1` 本机候选为 `68,547,259` 字节，EXE SHA-256 为 `C2B3F5D720793FE18EBDBD71336F45F55498C1F074BB5831C86F1236FB55956D`；它使用 IconPark 官方轮廓图标并优先采用系统苹方字体，五个工作区实机检查与 `125/125` 解决方案测试均已通过，IconPark 授权文件同时内嵌并随发布目录提供，尚未上传或向玩家分发。
- 启动器 `0.8.2` 本机候选为 `68,547,782` 字节，EXE SHA-256 为 `53D27A7F51FFFCB72315C02CDEA751A33FC39E18D3F089C01A40C4097EDC04BD`；它放大了全局界面文字，修正了客户端准备步骤线与运行配置行的对齐，并针对 125% DPI 将苹方渲染改为 Ideal 字体度量、ClearType 抗锯齿和 Fixed 静态提示。五个工作区实机检查与 `125/125` 解决方案测试均已通过，尚未上传或向玩家分发。
- 启动器 `0.9.0` 已改为正式安装式候选，程序目录与游戏数据分离；每个档案使用独立 `.minecraft`，共享对象和受管 Java，并支持从旧 `%AppData%` 或自定义根目录迁移。单文件 EXE 为 `68,556,366` 字节，SHA-256 为 `73347225BDDFF2A0F43DB57F13B4CF41AB1BEE1B46FC0CD74A992647DE9496E2`；NSIS 安装包为 `61,782,139` 字节，SHA-256 为 `35240A3A21764A21ACB286FC30B1FC4755DE90844B49B075FB8B69174018B97C`。Windows 安装/卸载冒烟测试和 `135/135` 解决方案测试已通过；两份 EXE 当前均为 `NotSigned`，尚未上传或向玩家分发。
- 启动器 `0.9.1` 基于提交 `6adc93a` 增加 Minecraft 正常/异常退出记录、异常提示和玩家主动生成的本地脱敏诊断包；日志截尾、固定 ZIP 条目、世界存档排除、重解析点拒绝与 20 条退出/10 个诊断包保留上限均有自动测试。单文件 EXE 为 `68,591,719` 字节，SHA-256 为 `98C53E741F4880258568AA1EBC042E30102467AD139CF6A0AA8CCD02D484121F`；NSIS 安装包为 `61,795,645` 字节，SHA-256 为 `99E42CC78157050041F13504AE9ED93F1F1573D7244F06EA5E692EBF8291929F`。Windows 安装/卸载冒烟测试和 `140/140` 解决方案测试已通过；两份 EXE 当前均为 `NotSigned`，尚未上传或向玩家分发。
- 启动器 `0.10.0` 基于提交 `9cba23e9d0b5ba799af50dcc2ef0018cfe5a31e4` 增加退出当前设备、退出所有设备及密码确认解除 Minecraft 绑定；服务端会原子撤销启动器会话、管理员会话、后台登录票据和 Velocity 进服授权。单文件 EXE 为 `68,607,280` 字节，SHA-256 为 `D9FA21C5F15E3B30FFED8FEF4E011672B75C4A15987712BBF574A0CEDD3834F3`；NSIS 安装包为 `61,796,065` 字节，SHA-256 为 `E2E14306882EF072016F35D740D2F06A7C8D12F63FFE28DD0F6A2C07B24D4876`。`157/157` 解决方案测试、账户页实机检查、`0.9.1` 原地升级、干净安装、卸载、IconPark 授权文件和用户数据保留验收均通过；卸载后程序目录、快捷方式和注册表无残留。Windows EXE 均为 `NotSigned`。安装包已上传私有 OSS，匿名直链返回 `403`，当前仅通过最长 24 小时的签名链接供内部灰度，尚未公开发布。完整记录见 [`LAUNCHER_RELEASE_0.10.0.md`](LAUNCHER_RELEASE_0.10.0.md)。
- API `0.10.0-20260724T101528Z` 单文件为 `103,635,312` 字节，SHA-256 为 `ECE445F76682775917D089630B6C0105AEE04707EE08D36886E53514E8CDCB11`，归档 SHA-256 为 `020DD8BA3D8D797336B5155F60EC34F900D9B27310FB52085B6BAA1BFEA8A4E6`；生产回归发现 Npgsql 带参数多语句预处理问题。
- API `0.10.1-20260724T102830Z` 基于提交 `19709c2` 将相关事务拆为单语句命令。单文件为 `103,634,800` 字节，SHA-256 为 `07452219F072D2CD91E53F427819DC2F13B9E887D278D2F817110F462AC7CBE3`；归档为 `45,282,743` 字节，SHA-256 为 `5EAF4651D076B1F72CDFF83ED1D628D046621286C58B6BACC0DB03453FEC36A9`。生产隔离回归、精确清理、公网与旧业务回归全部通过，现保留为历史回滚版本。
- 启动器 `0.11.2` 基于提交 `9622e54c1c9726e33a6f2848dae2720fa8405f7f`。单文件 EXE 为 `68,633,838` 字节，SHA-256 为 `5E71FB31B4983AB4115B9BD314E9756558E8B2D4EDBCE5C1B089CEAA5ACBBE65`；NSIS 安装包为 `61,800,211` 字节，SHA-256 为 `FC3E2AD75A9E35C3FD6FCD6FBF8375BB6BE7BCE651E922EA07F865D873A1F3DC`。`185/185` 测试、覆盖升级、基础档案全量落盘、并行续传和 Java 兼容路径验证均通过；两份 EXE 均为 `NotSigned`，尚未上传 OSS。
- 启动器 `0.11.3` 基于提交 `876d905190e246cf906167183546e8b7c0e41db9`。单文件 EXE 为 `68,636,908` 字节，SHA-256 为 `CEB4AD2B69260941028CED2E4BBC7F8A11F39450FF79F8B9D4A89C3B3194733E`；NSIS 安装包为 `61,803,987` 字节，SHA-256 为 `FDEB88A559A94D37FD70F059104E8DAD68AD347FA6E51ABF8943F166606824A9`。`191/191` 测试、覆盖升级、动画中间值、服务器与账号页实机验收均通过；两份 EXE 均为 `NotSigned`，尚未上传 OSS。
- 启动器 `0.11.4` 基于提交 `f21097225312659220fdb82ea630feffba1d5024`。单文件 EXE 为 `68,637,409` 字节，SHA-256 为 `2FB18541D8FC0B1D398C25FF567B0F626CA9FCBA32F80B9EC6BDED05470223FA`；NSIS 安装包为 `61,804,910` 字节，SHA-256 为 `D6249C33C97FD375A295B9BD7FB9B8236E8B981C964227CB3B776CA276E9A0D9`。`192/192` 测试、真实 WPF 像素渲染、覆盖升级及登录/注册两种选中状态实机截图均通过；两份 EXE 均为 `NotSigned`，尚未上传 OSS。
- 启动器 `0.11.6` 基于提交 `6998dc344c40b49eda0137bb239f3ddd058d248f`。单文件 EXE 为 `68,637,418` 字节，SHA-256 为 `58C1063B5BC65684C55FC685DCB0E5F45CD4151B3DAC8E361030D0BFF1A59F67`；NSIS 安装包为 `61,802,610` 字节，SHA-256 为 `32E06CF9DCE0811293E1279C4C76B8B2C5C8401859FC5A84DCE64AB1227416E9`。`193/193` 发布构建测试、等宽完整页签边框、表单整栏拉伸、覆盖升级及登录/注册两种状态实机截图均通过；当时主分支回归为 `.NET 200/200` 与 Velocity `11/11`。两份 EXE 均为 `NotSigned`。安装包已上传私有 OSS 固定键，匿名访问 `403`、短时签名下载 `200` 且远端大小与 SHA-256 复验一致，现已由 `0.11.7` 取代。`0.11.5` 仅为未上传、未打标签的本机失败候选。
- 启动器 `0.11.7` 基于提交 `bd54a780ae9124f9c01f4d0d1b63902b71fd5975`，修复失效 Minecraft 游戏凭据无法通过赫朝账号重登刷新的问题。单文件 EXE 为 `68,641,510` 字节，SHA-256 为 `C58A91B779884F6D91F11092F4496F56BB7F83CD879FCF93ACD9B0BC28DA5D3F`；NSIS 安装包为 `61,805,936` 字节，SHA-256 为 `9215849E914C125D827CF86D104D5FFEF865840AEEE6F31A0DC2DA6F1B1819EA`。Debug 与 Release 完整测试均为 `203/203`，本机覆盖升级保留设置与 DPAPI 赫朝会话，两份 EXE 均为 `NotSigned`。安装包已上传私有 OSS 固定键，匿名访问 `403`，24 小时签名下载 `200` 且远端长度与 SHA-256 复验一致，当前替换 `0.11.6` 进入 2 至 3 人灰度。
- 启动器 `0.11.10` 基于提交 `efdde7662d097638d181d93af3f5e2ae695df8cf`，统一运行配置侧栏的 Java 与内存分段选择器并修复边框裁切。单文件 EXE 为 `68,679,985` 字节，SHA-256 为 `A3B2DA5260DEFC694A8D1C15257FFC31E91C6ADDAC5D66489B0A5A38379BF7B7`；NSIS 安装包为 `61,819,393` 字节，SHA-256 为 `4703FEF3113418BB13DBA86F097BE45D2C66BFD020774354117A0001FAA127AA`。Debug 与 Release 完整测试均为 `218/218`，本机覆盖升级保留设置和 DPAPI 会话；安装包已上传私有 OSS 固定键，匿名访问 `403`、24 小时签名下载 `200` 且远端长度与 SHA-256 一致，当前替换 `0.11.9` 进入内部灰度。
- 启动器 `0.11.11` 基于提交 `acb2415f7deb391114c8b24d5839ae1928087e74`，增加玩家主动回滚、运行中保护、Java 复验和更强的原子恢复边界。单文件 EXE 为 `68,691,239` 字节，SHA-256 为 `54DB5D44FEFF5B0BCCE3641D97ADEFB22FEF4FEE96D0D44F179D53D8CE3FBDC5`；NSIS 安装包为 `61,823,943` 字节，SHA-256 为 `F6687C4CBB53BEFB3DC3D8B84FFBDF0AEC589DF69D710EE4F5DF43EFD47CB894`。Debug 与 Release 完整测试均为 `226/226`，本机覆盖升级保留设置和 DPAPI 会话；安装包已上传私有 OSS 固定键，二次发布校验后跳过，匿名访问 `403`、24 小时签名下载 `200` 且远端长度与 SHA-256 一致，当前替换 `0.11.10` 进入内部灰度。
- 启动器 `0.11.12` 基于提交 `2e21c5b93980bb62120e0f5cb9ee124658966d20`，增加玩家确认诊断上传、上传前本地 ZIP 复验、鉴权刷新和独立流式上传客户端。单文件 EXE 为 `68,711,016` 字节，SHA-256 为 `BD02D3352271B53D829D543DDE35A216205508330D468E34DF151056EC7AE6DF`；NSIS 安装包为 `61,833,814` 字节，SHA-256 为 `F54297318865995225CE8CB748C115EA4DCA8219E02AE09ABE266F783EC033D6`。Debug 与 Release 完整测试均为 `251/251`；隔离目录干净安装、从 `0.11.11` 覆盖升级、两轮卸载、设置与 DPAPI 会话保留均通过。安装包已上传私有 OSS 固定键，二次发布校验后跳过，匿名访问 `403`、24 小时签名下载 `200`，远端长度和 SHA-256 一致，当前替换 `0.11.11` 进入内部灰度。
- 启动器 `0.11.13` 制品基于提交 `dc1d1d527d106fbbc41a58add4bfbd7ae2d12cc9`，增加隐私受限运行遥测、离线队列、幂等批次和固定失败分类。单文件 EXE 为 `68,755,358` 字节，SHA-256 为 `985D4EEA3340D3AC0312DD90954729A348DB04471B947CA1AE52E361ABEAD1DD`；NSIS 安装包为 `61,868,113` 字节，SHA-256 为 `E6BF44D9971CEF6D874368E9912158BC60B88A886C652318E94F9D4BE0FFCFE7`。完整解决方案 `346/346`；可重复验证脚本完成干净安装、从 `0.11.12` 覆盖升级、两轮卸载、设置与 DPAPI 会话保留。安装包已上传私有 OSS 固定键，二次发布校验后跳过，匿名访问 `403`、24 小时签名下载 `200`，当前替换 `0.11.12` 进入内部灰度。
- 启动器 `0.11.14` 制品基于提交 `6f337f337e15e4f7151d5df8a04db5fd40df98a7`，让“启动时检查客户端更新”真实控制首次扫描，同时保留进服前强制检查。单文件 EXE 为 `68,755,863` 字节，SHA-256 为 `4F5B24E0DB08884851B8995619F026067F55DEA0A43713E64A3B24092C27869A`；NSIS 安装包为 `61,866,744` 字节，SHA-256 为 `82542FEBDD826AF4C40D8E0AFCD65990BE54A748734829FA7EC46214A27E5EDB`。完整解决方案 `348/348`；可重复验证脚本完成干净安装、从 `0.11.13` 覆盖升级、两轮卸载、设置与 DPAPI 会话保留。安装包已上传私有 OSS 固定键，二次发布校验后跳过，匿名访问 `403`、24 小时签名下载 `200`，当前替换 `0.11.13` 进入内部灰度。
- 启动器 `0.11.16` 制品基于提交 `ca71962dd17ac4ed79282fd33cc16500f45fbdd0`，修复 Minecraft 退出后当前档案状态不复位，并继承 `0.11.15` 的 Mojang 日志配置恢复。单文件 EXE 为 `68,763,504` 字节，SHA-256 为 `E75DBF4EFC622AB3DA5E79AC6443443992EE294A0E38C614FBF5F204ADF9091E`；NSIS 安装包为 `61,866,222` 字节，SHA-256 为 `6D7C9E91EA621B384633F86D6498EBBD55BF73B65516D8F24F0838CD48EA4D8A`。完整解决方案 `369/369`；可重复验证脚本完成干净安装、从 `0.11.15` 覆盖升级、两轮卸载、设置与 DPAPI 会话保留，本机正式安装也已复核。安装包已上传私有 OSS 固定键，二次发布校验后跳过，受保护结果 ACL、匿名 `403`、24 小时签名下载 `200`、长度与 SHA-256 均通过，现为 `0.12.0` 的直接回滚版本。
- 启动器 `0.12.0` 制品基于提交 `ba9576cd525de78fa639453e54466d967d5f1541`。单文件 EXE 为 `68,781,119` 字节，SHA-256 为 `56A2AAA6AB1CBF939DC11EA2430B47DDC96EF45AD5415D259878E6ED00855953`；NSIS 安装包为 `61,874,184` 字节，SHA-256 为 `9843E51B044611945BA8745D766678B4F43FB465E9039DD7EF3BDF9E55EAD4C2`。完整解决方案 `379/379`，覆盖升级、干净安装、双轮卸载、设置和 DPAPI 会话保留通过。安装包已上传私有 OSS 固定键；匿名 `403`、两次签名回读 `200`、长度和 SHA-256 一致，重复发布校验后跳过。当前已发布，待真实四级账号和多人灰度。
- 启动器 `0.12.1` 制品基于提交 `51a82cf5e3b30e62db37db1bf6911e9a661eb818`，修复含 U+200C 等格式字符的数据根目录中 NeoForge Jar-in-Jar 临时文件与 LWJGL 原生库加载冲突。单文件 EXE 为 `68,783,690` 字节，SHA-256 为 `86FDE25FC5C6A929C649FC53C599B9563A56AF2C64FCB052631D09DAB8C7FDEE`；NSIS 安装包为 `61,874,773` 字节，SHA-256 为 `6C5783AD9F0B21F0E7DB6BB4F9FC6E7A62BEFF3B550169F48D4F9CF8DBF1B907`。完整解决方案 `384/384`，Activity 冷/热两轮真实 NeoForge 启动、`0.12.0 -> 0.12.1` 覆盖升级、干净安装、双轮卸载、设置和 DPAPI 会话保留均通过。安装包已上传私有 OSS 固定键；匿名 `403`、两次签名回读 `200`、长度和 SHA-256 一致，重复发布校验后跳过。`0.12.0` 为直接回滚版本。
- 启动器 `0.12.2` 制品基于提交 `6405ac760fba9422d3f82fb6d3b9111e79ee700f`，最终规范 NeoForge 的 `java.library.path`、`org.lwjgl.librarypath`、JNA、LWJGL 解压与 Netty 五个原生目录属性，并加强过期游戏进程状态清理。单文件 EXE 为 `68,785,734` 字节，SHA-256 为 `EF8817CB19AC6A51C09CBEDD8685151044C7864B76F4C90293804E286923FEF3`；NSIS 安装包为 `61,876,002` 字节，SHA-256 为 `FEE5A53FF9A6033E96E2150E8A31D474B559581BEED14B65F939743A83C4BDCB`。完整解决方案 `386/386`，源码运行时冒烟、`0.12.1 -> 0.12.2` 覆盖安装、设置和 DPAPI 会话保留、安装版 Activity 真实进服、退出码 `0` 与零残留进程均通过。安装包已上传私有 OSS 固定键；匿名 `403`、两次签名回读 `200`、长度和 SHA-256 一致，重复发布校验后跳过。`0.12.1` 为直接回滚版本。
- 启动器 `0.12.3` 制品基于提交 `e6a160c46b89e2d5e607363e662b327760930324`，将 Activity 的 LWJGL 原生 DLL 从可能解析回含 U+200C 真实目标的目录联接切换到 `%LocalAppData%\Hechao\Launcher\native-runs` 物理目录，并统一五个 JVM 原生目录属性。单文件 EXE 为 `68,793,918` 字节，SHA-256 为 `18CF9772099EA1CA6FFEC7B8588CFFBFC3137FDACCD733291CC1242423518E07`；NSIS 安装包为 `61,874,260` 字节，SHA-256 为 `18E786560AF14C246EFF84638BABBE8E1CC02CBFB1E1065AD9501468C20603C6`。完整解决方案 `392/392`、原生目录专项 `18/18`、源码运行时冒烟、`0.12.2 -> 0.12.3` 覆盖安装、设置和 DPAPI 会话保留、安装版 Activity 真实进服、退出码 `0` 与零残留进程均通过。安装包已上传私有 OSS 固定键；匿名 `403`、两次签名回读 `200`、长度和 SHA-256 一致，重复发布校验后跳过。`0.12.2` 为直接回滚版本，但回滚后 Activity 必须保持关闭。
- API `0.11.1-20260725T165050Z` 基于提交 `3f17cba`。单文件为 `103,711,283` 字节，SHA-256 为 `0336CBE79E02F2E9F7F7C37490120FAA840CF083C84B02537ACFEA5266B75F45`；归档为 `45,309,559` 字节，SHA-256 为 `E727E9B840E81CDEFE5D45586AEF874B6E082D29562F797F45ECC8C98589E587`。生产健康、数据库、旧网站、中转 API、管理员入口和 journal 回归均通过，现保留为历史版本。
- API `0.12.0-20260725T203001Z` 单文件为 `103,716,915` 字节，SHA-256 为 `B46A22280243BA9801EB66FD628ED598CD27F0FED7995788C4452D222C3B27D1`；归档为 `45,382,027` 字节，SHA-256 为 `C76DA133466A4D609F8009A5206FDAFCDDE72DC0CB7D78FBC8E8C8B473DA5D41`。授权定向路由、健康/就绪、旧业务与生产合成授权回归通过，现保留为历史版本。
- API `0.13.0-20260726T173536Z` 单文件为 `103,796,275` 字节，SHA-256 为 `F2B7466A9AFAB142F110D7C2EB692DE1BA2FDD653F7CF42D4AE31D5BF7E8C811`；归档为 `45,339,427` 字节，SHA-256 为 `E7C8DECAFD8A3B47EB63987F8542C8BB034AB86C831F32B242F741FE26ABC728`。诊断上传、清理、生产错误路径和旧业务回归通过，现保留为历史版本。
- API `0.14.1-20260726T190856Z` 单文件为 `103,854,131` 字节，SHA-256 为 `F02CC7AAC3AE4FC8726548E3777D231D035B03E19487CAB32627333CEBBB8A3A`；归档为 `45,365,349` 字节，SHA-256 为 `877C9EBE6CDB5B611E0495F69BC759D6D51B4A1A1AF8D60A906F7BEC57E8959E`。排期、公告、玩家搜索、访问预览、单服规则、迁移 10、独立端口启动预检和公网回归通过，现保留为历史版本。
- API `0.15.0-20260726T202540Z` 单文件为 `103,950,899` 字节，SHA-256 为 `42ACC44468989A567E936993934046266A9D2B22B43758322E693BC23A089FD6`；归档为 `45,404,470` 字节，SHA-256 为 `9B096CBB55636494D64148908DA7168D5B748E12086BE6C854417891FEBBF10A`。账号安全、迁移 11、生产备份还原后的隔离端到端验收、原子部署和公网回归通过，现为 `0.16.0` 的直接回滚目标。
- API `0.16.0-20260726T222124Z` 单文件为 `104,046,643` 字节，SHA-256 为 `8B932BC0BFE5C0D3D2A97460555695AD33544CC2814A96B8AF16E672F8B5CDB5`；归档为 `45,433,983` 字节，SHA-256 为 `2B1568D5A72DAE09E0CED270633099AA961F11EBC730490451E1448BDA9EE4D4`。论坛 Cookie 联动、受控全局等级、迁移 12 至 13、生产 worker 投递、原子部署和公网回归通过，现为历史版本。
- API `0.19.0-20260727T005013Z` 基于提交 `7ba2eba`。单文件为 `104,346,675` 字节，SHA-256 为 `29B351C33B6366BF2C3E9263275928D0F5C8329D05C14B1C7A138C0D81B279FA`；无 PDB 归档为 `45,550,337` 字节，SHA-256 为 `B8A82819AB0CD42F09A1B435A29CFFC26C6335215157EB8FF5FE1F48B9755455`。迁移 16、服务器运行样本、服务状态后台、隔离生产副本、原子部署和公网回归通过，现保留为历史版本。
- API `0.20.0-20260727T011953Z` 基于提交 `b5c1a78`。单文件为 `104,441,907` 字节，SHA-256 为 `67C3E084D9E53509B283A4B39498219C33BF1676BB4F1805A916E83CFFABBDEB`；无 PDB 归档为 `45,575,077` 字节，SHA-256 为 `874C05ECE9AA6DE628C1B7E99191D8E7FB50745E7224059D1F5D8C90E95A8665`。迁移 17、请求指标、统一告警、后台告警页、平台监控器、隔离生产副本、原子部署和公网回归通过，现为 `0.20.1` 的直接回滚目标。
- API `0.20.1-20260727T145451Z` 基于提交 `f90a2de9eae0fb6044f0fdf7571708b91da50b10`。单文件为 `104,442,419` 字节，SHA-256 为 `94BC3831A4749A545968E90BD1ABD638BE26BD23B058091E2A91AF417D09AB54`；无 PDB 归档为 `45,575,206` 字节，SHA-256 为 `035C71CFCAB3ACF2986AE9936833CAD004B4B9087F08385F0FFC9DA39C46F6FC`。私有下载重定向不再记录签名目标，Nginx 查询参数和 Referer 脱敏、`355/355` 自动测试、原子部署与公网回归均通过，现为直接回滚目标。
- API `0.20.2-20260727T225819Z` 基于提交 `c2b50e2ac75b8bc9a66cfcb9691c7ee566ebfd57`。单文件为 `104,444,979` 字节，SHA-256 为 `327D17A6F24833CDAD9F912AC16D87EC2DEE463F7DBD427B6E672307DA24A6F6`；无 PDB 归档为 `45,574,298` 字节，SHA-256 为 `AE5561DFA85FB59476C22D66CB2AF0781112345B82BED8E9D7825DBC34559B32`。客户端会话来源、Minecraft 版本和模组档案兼容保护、`360/360` .NET、`13/13` Velocity 及生产矩阵 `8/8` 通过，现保留为历史版本。
- API `0.22.0-20260729T144953Z` 基于提交 `ba9576cd525de78fa639453e54466d967d5f1541`。单文件为 `104,453,683` 字节，SHA-256 为 `CCD8EFAF4D1F3F89A1BF7C08F2F407283892F3CC69733155ACA6884D45073A13`；无 PDB 归档为 `45,576,531` 字节，SHA-256 为 `2C70353CDB1C9458ADF1B14DB358CB13A31B91ECB22AC25267FF824AE4058B03`。迁移 `019`、玩家/基础设施角色、Lobby 隐藏后监控、公开目录零 Lobby、健康/就绪、旧业务回归和 journal 零新增错误通过，当前在线。
- 正式基础档案为 `base-1.21.11` / `1.0.5`，清单 SHA-256 为 `65667E6198C3ECF75DF79C686C87C244F3D5AC21B170364BD998A1DF5111640E`；包含 `4,902` 个文件、`4,900` 个去重对象和 `874,147,856` 字节。
- NeoForge 活动正式档案为 `activity-neoforge-1.21.11` / `1.0.10`，清单 SHA-256 为 `0E059BBFE9FAB6770204DE547567CA64420A45E8364FA93206BB316E8AE2B69F`；包含 `4,754` 个文件与对象、`621,732,083` 字节。Meccha SHA-256 `C72511BEF3B0CC2C1A1C97E1C33709901714460191F9549FD461E71215534E9E` 与活动服一致；生产信任验签、发布物闭合验收、全量安装、逐文件复验、NeoForge `21.11.42` 进程构建、分级授权和真实 OSS 下载均通过。发布时目录保持 `Closed 0/30`；2026-07-28 已受控开服并取得指标，正确客户端真实进服仍待验收。
- 恐怖整蛊正式档案为 `pvp-fabric-1.20.1` / `1.0.0`，清单 SHA-256 为 `A5BCBBA71C69E85F0ACE4000C1983F8C9C1C1D7F546AFA36C53AE39C895706E6`；包含 `3,749` 个逻辑文件、`3,748` 个去重对象和 `885,821,291` 字节。加载器为 Fabric `0.16.14`，运行时为 Java `17`；生产上传新增 `3,547` 个对象、校验跳过 `201` 个对象，当前目录显示名为“恐怖整蛊”。
- Vanilla 正式档案为 `vanilla-1.21.11` / `1.0.0`，清单 SHA-256 为 `C22DEDC09576273B6D4C52B07CF7975D09BA758533B7395974BE34F73344C865`；包含 `4,671` 个文件与对象、`549,101,696` 字节，运行时为 Java `21`，已绑定 Survival1。
- Forge 正式档案为 `forge-1.20.1` / `1.0.0`，清单 SHA-256 为 `D33FF592B115667713BCC87477710AA7D8A86F77490C23B70B7DEE620A56919C`；包含 `3,667` 个文件与对象、`725,771,107` 字节，加载器为 Forge `47.4.0`，运行时为 Java `17`。当前未绑定服务器，因此不会出现在玩家目录。
- DollNight 正式档案为 `dollnight-1.21.11` / `1.0.0`，清单 SHA-256 为 `6D0C73C2B8CD34621C5D44212047DC562AD05E8277B1F195BDAC0FDA5DA16575`；包含 `4,902` 个逻辑文件、`4,900` 个去重对象和 `874,147,856` 字节，加载器为 Fabric `0.19.2`，运行时为 Java `21`，已绑定 DollNight。三份新增档案的生产信任验签、闭合校验、全量安装、逐文件复验、进程构建、权限和对象下载回归均通过；当前解决方案测试为 `251/251`。
- 独立发布 RAM 用户 `hechao-launcher-publisher` 当前绑定
  `HechaoLauncherOssObjectPublish` v5，只可对 `hechaoworld/objects/*`、
  `hechaoworld/releases/launcher/*`、`hechaoworld/backups/database/*` 与
  `hechaoworld/backups/services/*`、`hechaoworld/backups/recovery/*` 执行
  `oss:GetObject` 与 `oss:PutObject`。控制台回读已确认 v5 为默认版本；没有列举、
  其他前缀读取、删除、ACL、版本管理或整桶权限。AccessKey 仅以 Windows DPAPI
  `CurrentUser` 密文保存在管理员电脑，并以 root-only 独立环境提供给异地备份服务。
  API 继续使用 `hechao-launcher-distribution` 只读身份；两份生产 AccessKey ID 哈希
  均已与本机受保护凭据匹配，未重启 API。首批基础档案 `4,900` 个对象共
  `874,147,706` 字节；活动档案随后提交 `4,754` 个对象和 `621,732,083` 字节；
  启动器 `0.10.0` 私有安装包为 `61,796,065` 字节。
- Bucket 版本控制已开启，OSS 会忽略 `x-oss-forbid-overwrite`。活动首次上传因此为 `4,551` 个共享摘要创建同内容新版本，真正新增 `203` 个摘要、`152,843,997` 字节。发布器 `0.7.0` 已改为先校验当前对象长度与 SHA-256 元数据，匹配则跳过，不匹配则硬失败；生产全量复验结果为 `0` 个上传、`4,754` 个跳过、`0` 上传字节。完整记录见 [`ACTIVITY_PROFILE_RELEASE_1.0.10.md`](ACTIVITY_PROFILE_RELEASE_1.0.10.md)。

- 启动器当前正式版本为 `0.15.5`，制品源码提交为
  `6c71303959b89861316405541a3693f6f9900be5`，正式标签为
  `launcher-v0.15.5`。单文件 EXE 为 `69,016,845` 字节，SHA-256 为
  `D77D1F984B4CE07B6C40FE21691F4014550010DA80873EF55E1DEEC3B5F73152`；
  NSIS 为 `61,963,099` 字节，SHA-256 为
  `03724C0D97A32270103012426E891A55F088AE4470796C9415BB609E828DC5AF`。
  两者均为 `NotSigned`。完整解决方案 `710/710`，启动器 `225/225`。
- `0.15.5` 私有 OSS 对象匿名读取 `403`、两轮签名回读 `200`，长度与
  SHA-256 一致；`0.15.4 -> 0.15.5` 覆盖安装、全新安装、双轮卸载、设置和
  DPAPI 会话保留已验收。`0.15.4` 保留为上一正式版本，其不可变对象与标签不得覆盖。
- 生产更新通道当前为 `LatestVersion=0.15.5`、
  `MinimumSupportedVersion=0.12.3`；API 服务为 `active`、`NRestarts=0`，
  内外网健康与就绪端点均为 `200`。详细记录见
  [`LAUNCHER_RELEASE_0.15.5.md`](LAUNCHER_RELEASE_0.15.5.md)。

### 1.8 服控内存管理制品

- API `0.23.2-20260731T050744Z` 归档为 `45,641,384` 字节，SHA-256
  `D4E1AF0A8E02820C04D52F199581D45D5436887D0C4B9C5730736F5B6D0E2DD5`；
  单文件为 `104,595,507` 字节，SHA-256
  `6C3CB5B93086EB3CA96428AEDF409C74FAD96527E0547916FE07162AF57B0AE6`，现保留为回滚版本。
- API `0.24.0-20260731T062107Z` 归档为 `45,644,864` 字节，SHA-256
  `3C852B98AA7BC99DB3EF8CE9EB3BC500262A12CE29D55905D03D5CC16D1439B4`；
  单文件为 `104,597,043` 字节，SHA-256
  `A90E7C61FECF811C183293403CD4B1816EFF6047DB5788A415BD6EFC1D34B66A`，当前作为直接回滚版本。
- API `0.24.1-20260731T105946Z` 归档为 `45,644,480` 字节，SHA-256
  `85E6BC7EE935678BB275A09611FDAF6DF39D38D24C104A7C24D2A10C0B93CAF7`；
  单文件为 `104,606,771` 字节，SHA-256
  `E5449CA15BE8B60154601EC54C2B0408A9E4D1C1DAB090A4835602CF31CE15DB`，当前在线。
- 服控代理 `0.2.3` ZIP 为 `33,142,794` 字节，SHA-256
  `DCFCB19AE8F3301111E9283FE7C2E24B8A1F6E6746FC944003BD44686E9D27E0`；
  EXE 为 `73,899,214` 字节，SHA-256
  `633A9C7EB63D982E2E9A0AC450E54679E74DBE4BD21DD38EEAFF6A572F9647F1`。
- owl5 运行代理 `0.2.3`，owl9 保持 `0.2.1`；owl5 的空服关停阻塞已修复，升级前后
  其余 5 个 Java PID 和启动时间不变。回滚备份、根因和验证见
  [`SERVER_CONTROL_AGENT_RELEASE_0.2.3.md`](SERVER_CONTROL_AGENT_RELEASE_0.2.3.md)。

## 2. 主 Minecraft VPS：owl5

| 项目 | 当前值 | 证据状态 |
| --- | --- | --- |
| 系统 | Windows Server 2022 Standard | 已实时读取 |
| CPU 配额 | 6 核 / 6 逻辑处理器，宿主型号 Ryzen 9 9950X | 已实时读取 |
| 内存 | `19,326,763,008` 字节，核查时空闲 `12,388,392,960` 字节 | 2026-07-27 实时读取 |
| `C:` | `42,011,193,344` 字节，剩余 `8,503,496,704` 字节；虚拟 SSD / SAS，健康 | 2026-07-27 实时读取 |
| `E:` | `75,158,777,856` 字节，剩余 `22,302,515,200` 字节；虚拟 SSD / SAS，健康 | 2026-07-27 实时读取 |
| SSH | 外部端口 `15152`，Windows 内部 `22` | 已验证密钥登录 |
| RDP | 外部端口 `15153`，Windows 内部 `3389` | 连接方式已记录，未在本轮登录 |

### 2.1 Velocity 与后端目录

| 逻辑服务 | 目录 | 内部端口 | 核查时状态 |
| --- | --- | --- | --- |
| Velocity | `E:\Velocity` | `25577` | 运行中，`-Xmx1G` |
| 大厅 | `E:\LobbyServer` | `25566` | 运行中，`-Xmx2G` |
| Survival1 | `E:\Survival1` | `19228` | 已停止；目录状态 `Closed` |
| Survival2 | `E:\Survival2` | `25565` | 运行中，`-Xmx2G` |
| Activity NeoForge | `E:\ActivityNeoForge` | `25568` | 运行中；零玩家时 Tick 指标暂停 |
| DollNight | `E:\DollNight` | 与 Survival2 共用 `25565` | 替换服，不可与 Survival2 同时运行 |

Velocity 路由基线：`lobby -> 127.0.0.1:25566`、`survival1 -> 127.0.0.1:19228`、`survival2 -> 127.0.0.1:25565`、`activity -> 127.0.0.1:25568`、`pvp -> owl9.vipi9.top:19243`。公网入口保持 `mc.hehe11.fun`，Minecraft SRV 记录指向外部端口 `15156`；2026-07-26 重启后从外网验证该端口 TCP 可达，默认 `25565` 不直接开放。

保留替换服目录：`E:\ActivityHybrid`、`E:\ActivityLocal`、`E:\ActivityServer`、`E:\MonsterActivity`。这些目录不能与占用相同入口端口的当前活动后端同时启动。

Velocity 授权插件 `HechaoVelocityAuthorizer-0.4.0.jar` 已放入 `E:\Velocity\plugins`，
大小 `22,967` 字节，SHA-256 为
`D3CEB0624A0AD70045897521795F275BC61973CF119873114149BDAEEAA95120`；配置位于
`E:\Velocity\plugins\hechao-velocity-authorizer\config.properties`，模式为
`monitor`；首次授权故障、基础设施目标、`MinecraftVersionMismatch` 和
`ClientProfileMismatch` 均立即拒绝。备份位于
`E:\manual-backups\VelocityAuthorizer-0.4.0-20260729T150949Z`，旧 HubCommand、
ViaVersion 和 ViaBackwards 备份位于
`E:\manual-backups\LegacyLobbyRouting-20260729T151223Z`。计划任务
`Codex-Velocity-Live` 监听 `25577`；启动日志确认插件以 `owl5-main` 初始化。

大厅独立守卫为
`E:\LobbyServer\plugins\HechaoLobbyGuard-0.1.0.jar`，大小 `3,047` 字节，
SHA-256 为
`B0B7AA651994797B16B1271D332EF03A218F8BB8FEC3226CF0F705D74311DE99`。
备份位于 `E:\manual-backups\LobbyGuard-0.1.0-20260729T151317Z`；大厅仅监听
`127.0.0.1:25566`，强制白名单且名单为空。LuckPerms 等级代理、指标、告警和备份
继续运行，但玩家、OP 和管理员均不得进入。

本次激活前代理没有已建立的玩家连接；重启只作用于 Velocity，大厅、生存服、活动服和其他 Java 进程均未被操作。

LuckPerms 使用各 Paper 服共享的本机 MariaDB；启动器同步桥位于 `C:\ProgramData\Hechao\LauncherBridge`，计划任务 `Hechao Launcher LuckPerms Sync` 以 `SYSTEM` 身份每 5 分钟只读同步。当前快照共 114 人：`default=99`、`vip=12`、`admin=1`、`owner=2`。同步任务不控制任何 Minecraft 进程。

受控等级代理 `E:\LobbyServer\plugins\HechaoLuckPermsTierAgent-0.1.0.jar` 的
SHA-256 为 `35A9BBB17620DC2FD7245E0EA8CCAA293DC98C264DA3463AB706846ED7E42A7B`；
配置位于同目录的 `HechaoLuckPermsTierAgent\config.properties`，ACL 已收紧，备份位于
`E:\manual-backups\luckperms-tier-agent-20260726T223127Z`。安装时没有重启大厅；
2026-07-28 的受控重启后启动日志已确认代理加载，真实 owner 快照可用。

状态采集器 `0.2.1` 位于两台游戏 VPS 的 `C:\ProgramData\Hechao\StatusCollector`，单文件 EXE SHA-256 均为 `7645909E8FE9690D022D7B14E065ACACAB85FA39F4D2C03B8E52BFBF9F3899ED`。两台计划任务 `Hechao Launcher Server Heartbeats` 都以 `SYSTEM` 身份每分钟运行：`owl5` 的 `mc-vps-primary` 查询 `lobby`、`survival2`、`survival1` 和 `activity`，`owl9` 的 `owl9-pvp` 只查询本机恐怖整蛊历史目标 `pvp`。Activity 单独启用零玩家暂停识别；旧 Tick 数值不会发送，有玩家时仍严格报警。令牌使用 `LocalMachine` DPAPI 加密；采集器不包含 RCON 或进程启停能力。两机升级前后 Java PID 集合一致，回滚备份分别位于 `collector-0.2.1-20260729T211120Z` 和 `collector-0.2.1-20260729T211519Z`。

状态链路为游戏 VPS 主动向 `launcher-api.hechao.world` 发起 HTTPS POST，API 使用
独立心跳令牌认证；两台游戏 VPS 都不暴露 HTTP、指标或采集器监听端口。只读枚举
TCP 监听时没有发现 `Hechao.StatusCollector` 监听器，采集器仅查询各自主机上的
Minecraft 端口并向外上报。因此不需要为状态接口开放入站防火墙规则，也不存在
“只允许阿里云来源访问状态接口”的公网攻击面。

2026-07-27 的只读容量快照记录了四个 Java 进程的 `Xms/Xmx`、当前及峰值工作集、
累计 CPU 时间和读写传输量。Velocity 峰值工作集约 `653 MiB`；三个 Paper 后端
峰值分别约 `1.66 GiB`、`1.67 GiB`、`1.59 GiB`，均未触及各自 `2G` 上限。
机器可读数值见
[`evidence/INFRASTRUCTURE_CAPACITY_AND_WORLD_BACKUP_2026-07-27.json`](evidence/INFRASTRUCTURE_CAPACITY_AND_WORLD_BACKUP_2026-07-27.json)。

Paper/Purpur 指标代理 `HechaoServerMetrics-0.1.0.jar` 已复制到大厅、Survival2、
Survival1 的 `plugins` 目录，三份 SHA-256 均为
`BD03312007E043223B37CF634872C3DAA4C0FB11B80B54ADC546507853528B2C`，备份位于
`E:\manual-backups\server-metrics-20260727T004852Z`。部署时没有重启服务端；
2026-07-28 受控重启后已确认三服分别约为 `20 TPS`，MSPT 为 `1.1225`、
`1.0375`、`1.8530`。

NeoForge 1.21.11 指标代理
`E:\ActivityNeoForge\mods\HechaoServerMetrics-NeoForge-1.21.11-0.1.0.jar`
为 `12,684` 字节，SHA-256 为
`49C258C3AFF655070F40B576AC4A026AE8B5D43030A635800A7038451766027E`，部署记录在
`E:\manual-backups\mod-server-metrics-20260727T183833Z`。Fabric 1.20.1 指标代理
`C:\mc\server\mods\HechaoServerMetrics-Fabric-1.20.1-0.1.0.jar` 为 `13,183`
字节，SHA-256 为
`D38FB92413CC3B6B43CB87E396957697455A30799415611CB43C55D2C895B3F6`，部署记录在
`C:\manual-backups\mod-server-metrics-20260727T183834Z`。两个受限备份目录均已
禁止继承，暂存目录已清理；部署时没有启动游戏服。2026-07-28 受控开服后，
Activity 为 `20 TPS / 5.7745 MSPT`，恐怖整蛊为 `20 TPS / 12.7157 MSPT`。

2026-07-26 曾因两个每日任务并发创建约 3.4 GB 的不完整 ZIP，导致 `E:` 空间耗尽。两个损坏归档和一个 0 字节大厅归档已验证后清理；一份 `7,963,944,183` 字节的历史备份在核对文件名与大小后迁移到 `C:\manual-backups\E-drive-overflow`。2026-07-27 部署的 VSS 世界备份引擎位于 `C:\ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1`，大小 `35,370` 字节，SHA-256 为 `2CC7511C222FEE2D984FD49D150F89355D7C9C48FD7A705FDB3DB047C34CD691`。它会在 Essentials 短暂冻结期间创建 VSS 一致快照，握手后后台压缩，按源文件最坏情况做磁盘预检，使用全局状态锁、`.partial`、ZIP 条目复核、SHA-256 旁车、原子完成、独立保留和精确卷影清理。

2026-07-28 三服正式 Essentials/VSS 归档和异机隔离恢复已通过。管理机保存 Lobby
`3,352,746` 字节、Survival1 `3,668,228,973` 字节、Survival2
`4,946,045,967` 字节的完整归档并重算 SHA；全部 `10,347` 个条目已解压，七个
`level.dat` 有效，确定性抽检 `454/9,049` 个 `.mca`、`152,365` 个区块无问题。
当前 VPS 远端也各保留一份状态、ZIP、SHA 和条目一致的归档；`.partial`、
`active.json`、孤立旁车和专属 VSS 均为 `0`，`E:` 剩余
`13,684,019,200` 字节。详细证据见
[`evidence/WORLD_BACKUP_FORMAL_ACCEPTANCE_2026-07-28.json`](evidence/WORLD_BACKUP_FORMAL_ACCEPTANCE_2026-07-28.json)。

同日 owl9 恐怖整蛊 `C:\mc\server` 通过独立控制台保存/VSS 包装脚本生成
`E:\backups\horrorprank-backup-20260728-142039.zip`，大小 `4,149,156,327`
字节、`2,493` 个条目，SHA-256 为
`50FBC949071EB08D828D4A53F8AF001C8AC5AAF9A42443083A28714B8D32975A`。
管理机异机副本完成 `2,493/2,493` 文件长度与 SHA-256 比对、`level.dat` 校验和
`2,370/2,370` 区域文件全量检查。源端与恢复副本同有 `22` 个历史零字节
`entities`/`poi` 占位文件，路径完全一致且地形空文件为 `0`。备份前后 PID 均为
`7216`，真正 PVP `E:\MinecraftServer` 未触碰。包装脚本 SHA-256 为
`A3D25AAF6E6C58ADD16492EEEB095006EC8FF41449216ACB3716D50B55A3752F`，
回滚目录为
`E:\manual-backups\horrorprank-world-backup-wrapper-20260728T150500`。
`E:` 当前剩余 `5,787,840,512` 字节，再次完整备份前需计划异机复核和扩容或
精确回收。证据见
[`evidence/OWL9_HORRORPRANK_RUNTIME_AND_WORLD_BACKUP_2026-07-28.json`](evidence/OWL9_HORRORPRANK_RUNTIME_AND_WORLD_BACKUP_2026-07-28.json)。

同日复核的世界源文件量为 Survival1 `6,401,231,920` 字节、Survival2
`10,852,061,168` 字节、Lobby `11,556,833` 字节。磁盘上的计划已调整为
Survival2 02:00、Survival1 04:00、Lobby 05:30，Lobby Essentials 的旧
30 分钟循环已设为 0；变更前文件保存在
`E:\manual-backups\world-backup-schedule-20260726T062655Z`。本次没有热重载、
停止或重启服务，三个 Paper 后端与 Velocity 的监听 PID 均保持不变。

两个不参与运行的旧迁移 ZIP 共 `7,388,961,944` 字节已转存到管理机
`H:\server-backups\owl5`。两份本地文件均按 VPS 原件重新验证大小和 SHA-256，
并保存 `.sha256` 旁车文件后才删除远端原件。`E:` 最新只读核查可用
`22,302,515,200` 字节，约 20.77 GiB；三服最坏空间门槛约 17.39 GiB，
额外余量约 3.39 GiB。

## 3. 第二台 Minecraft VPS：owl9

2026-07-28 已使用现有运维公钥恢复实时管理基线，SSH 外部端口为 `19241`，
RDP 为 `19242`，主机名为 `WIN-802L81OVQVB`。系统是 Windows Server 2022
Standard `10.0.20348`，1 颗 AMD EPYC 7R13、4 个逻辑处理器、8.00 GiB 内存。
`C:` 为 39.13 GiB，盘点时剩余 8.88 GiB；`E:` 为 10.00 GiB，剩余 9.25 GiB。

2026-07-28 13:24 复核确认 owl9 存在两个不同的服务端：

| 服务端 | 根目录 | 核心 | 启动入口 | 当前状态 |
| --- | --- | --- | --- | --- |
| 恐怖整蛊 | `C:\mc\server` | Fabric `1.20.1` | 计划任务 `HorrorPrank` | 运行中 |
| PVP | `E:\MinecraftServer` | Purpur `1.21.11-2568-f57bd86` | `start.bat` | 已停止 |

两个服务端都配置内部端口 `25565`，因此不能同时启动，并复用公网游戏入口
`owl9.vipi9.top:19243`。赫朝启动器当前显示的“恐怖整蛊”历史内部 ID、
Velocity 目标和档案名分别是 `pvp`、`pvp` 与 `pvp-fabric-1.20.1`；这些标识
不代表 `E:\MinecraftServer` 的真正 PVP 服。完整强制边界见
[`OWL9_DUAL_BACKEND_OPERATIONS.md`](OWL9_DUAL_BACKEND_OPERATIONS.md)。

恐怖整蛊启动脚本使用随服 Java 21，参数为 `-Xms2G -Xmx5G`；
`server.properties` 为 `online-mode=true`、`max-players=20`、
`view-distance=8`、`simulation-distance=6`。真正 PVP 服使用
`E:\MinecraftServer\jdk`、`-Xms2G -Xmx4G` 和 Purpur 插件栈，当前没有赫朝
启动器目录记录、客户端档案或独立 Velocity 目标。

早期只读盘点时恐怖整蛊服处于关闭状态；后续受控验收已由 `HorrorPrank` 任务持久
启动。13:24 复核时只有一个 Java 进程，PID `7216`，可执行文件位于
`C:\mc\jre\jdk-21.0.11+10-jre` 并监听 `25565`，证明当前运行的是恐怖整蛊服；
真正 PVP 服未运行。本轮双服务端识别没有修改、启动或停止任何游戏服。

只出站状态采集器已于 2026-07-30 同步升级到 `0.2.1`，
`C:\ProgramData\Hechao\StatusCollector`。目录 ACL 仅允许 `SYSTEM` 和本机管理员，
一分钟计划任务以 `SYSTEM` 运行并连续返回 `0`。API 中代表恐怖整蛊的历史 `pvp` 行现由
`collector_instance=owl9-pvp` 独占，停服状态准确报告
`ProcessNotRunning`、`MetricsFileMissing` 和 C 盘容量；跨过两台采集器的完整周期后
没有再被 `owl5` 覆盖。`0.2.1` 升级前后恐怖整蛊 Java PID 均为 `7216`，没有启动、
停止或重启游戏服。该采集器的 `dataPath` 固定为 `C:\mc\server`，不代表真正 PVP 服；
若改开 PVP，必须先切换或停用恐怖整蛊的目录与心跳逻辑，禁止只凭共享端口冒充。

恐怖整蛊服的静态 Velocity 兼容改造已完成。官方 FabricProxy-Lite `2.6.0` 已安装到
`C:\mc\server\mods`，JAR SHA-256 为
`D4719179353D790453061C14B4148994FF431AC57A126555B3009CE9A748D6C7`。
原有配置在核对 `hackOnlineMode=true`、`hackEarlySend=true`、
`hackMessageChain=true` 和转发密钥摘要后原样复用，配置内容与
`server.properties`、启动脚本、计划任务定义均未改变。恐怖整蛊服继续保持
`online-mode=true`，没有有效 modern forwarding 数据的直连会被模组拒绝。
owl9 配置和 owl5 `forwarding.secret` 的 ACL 都已收紧为 `SYSTEM` 与本机管理员，
密钥内容未改变，Velocity 进程也未重启。

第一次正确恐怖整蛊客户端真实路由在 Velocity 与后端之间出现自定义包解码失败。已在
`save-all flush`、优雅停服和独立备份后安装官方 CrossStitch `0.1.6`；JAR 为
`5,321` 字节，SHA-1 为 `aba735301c683ed43d5f3361f532bf38f28116f2`。修复后的
`HorrorPrank` 持久任务同时列出 CrossStitch `0.1.6`、FabricProxy-Lite `2.6.0`
并在 SSH 退出后继续监听。真实会话由统一入口定向到历史目标 `pvp`（恐怖整蛊），稳定 `586` 秒且没有新的
解码错误；启动 UUID 与后端语音、缓存和玩家数据的内存哈希一致，公网直连被
`velocity:player_info` 明确拒绝，正常退出码为 `0`。一次随后重连在 Velocity
认证前超时，仍需补做成功样本。恐怖整蛊服先前空载基线为
`20 TPS / 12.7157 MSPT`；皮肤和有效游戏内权限仍需专用账号目视核对。
恐怖整蛊服为 1.20.1，而大厅为 1.21.11，
不能把不兼容的 `/hub` 当作可用回程。按已选方案完成隔离验证与真实客户端灰度后，再决定
是否收窄后端防火墙来源并推进 Velocity `enforce`。

脱敏机器证据见
[`evidence/OWL9_ASSET_BASELINE_2026-07-28.json`](evidence/OWL9_ASSET_BASELINE_2026-07-28.json)，
状态采集部署证据见
[`evidence/OWL9_STATUS_COLLECTOR_DEPLOYMENT_2026-07-28.json`](evidence/OWL9_STATUS_COLLECTOR_DEPLOYMENT_2026-07-28.json)，
恐怖整蛊 modern forwarding 部署证据（历史文件名保留 `PVP`）见
[`evidence/OWL9_PVP_VELOCITY_MODERN_DEPLOYMENT_2026-07-28.json`](evidence/OWL9_PVP_VELOCITY_MODERN_DEPLOYMENT_2026-07-28.json)，
首次真实路由与 CrossStitch 修复见
[`evidence/CLIENT_ACTIVITY_PVP_ROUTE_AND_PVP_FIX_2026-07-28.json`](evidence/CLIENT_ACTIVITY_PVP_ROUTE_AND_PVP_FIX_2026-07-28.json)。

## 4. 当前阻塞与风险

1. `download.hechao.world` 的 CNAME、HTTPS、私有 Bucket、读写分离 RAM 身份、真实客户端对象、签名清单和生产签名信任链已完成；生产签名加密恢复包已写入私有 OSS 并完成回读复验。
2. `owl5` 三份正式世界归档均为 `Completed`，远端 ZIP、SHA-256 旁车和条目数一致，异机完整解压与恢复检查已通过；当前 `E:` 仍保留约 12.74 GiB，后续继续按保留策略监控空间。
3. `owl9` 的密钥认证、双服务端实时盘点、恐怖整蛊只出站状态采集和 modern forwarding 均已完成；CrossStitch 修复后的真实进服、身份转发、直连拒绝、稳定连接和正常退出已通过。真正 PVP 服保持独立且未纳入本轮启动器验收。仍需恐怖整蛊多人断线重连及皮肤/权限目视核对；玩家回大厅和游戏内转服已经取消。
4. 启动器数据库、论坛与 Sub2API 的异地加密、真实 OSS 上传/下载、定时任务、告警恢复和异地主机隔离恢复均已验收；当前不再存在 RAM v5 或平台数据异地副本阻塞。
5. Microsoft 公共客户端已注册，Minecraft Java API 许可已由管理员确认通过；Velocity Authorizer `0.4.0` 已加载为 `monitor`，首次授权故障关闭、内部大厅永久拒绝和生产兼容矩阵已通过。Activity 与恐怖整蛊单账号真实首次路由、启动器 API 故障关闭与恢复均已验收。schema `2` 匿名灰度证据、四级/fresh grant/拒绝路径闸门、Velocity 模式事务和目录强制登录事务已实现，失败会保持或恢复原模式；四级账号、多人断线重连和逐级容量灰度仍待真实参与者，因此生产开关尚未启用。
6. 当前 `Hechao.Launcher.exe` 按已确认决策保持 `NotSigned`，Windows SmartScreen 首次运行提示属于已接受的首版发布风险；正式公告必须提供官方来源、大小和 SHA-256。客户端清单的 ECDSA 签名不能替代 EXE 代码签名，未来若增加 Authenticode 必须独立升版。

## 5. 当前 API 部署状态

> 启动器更新通道已在 `2026-07-31` 切换到 `launcher-v0.13.6`；下方 API
> 发布 ID 与其他组件标签保持各自独立版本。

- 发布 ID：`0.22.0-20260729T144953Z`
- API `0.22.0`、基础设施角色、客户端兼容保护、日志脱敏与平台监控器 `0.1.2` 已部署；启动器 `0.13.6` 为私有 OSS 当前版本。管理员 Web 已启用，真实 MFA 已登记。
- 启动器正式标签为 `launcher-v0.13.6`，制品源码提交为 `667a15a9eb48cfb2264c3d2f085abc7cbbe1c070`；API、Velocity 与各档案标签按 [`RELEASE_AND_GIT_WORKFLOW.md`](RELEASE_AND_GIT_WORKFLOW.md) 管理。
- 运行账户：`hechao-api`，无交互登录权限
- systemd：已启用并通过重启恢复测试
- 监听：仅 `127.0.0.1:8090`
- 公网 `8.148.207.171:8090`：连接超时，符合预期
- 公网入口：`https://launcher-api.hechao.world`
- `healthz`、数据库感知的 `readyz`：本机 HTTP 与公网 HTTPS 均为 200
- `GET /v1/catalog`：过渡阶段匿名请求返回玩家目录，无效 Bearer 返回 401；Lobby 为隐藏基础设施目标且公开命中为 `0`，正式强制开关待四级灰度和 Velocity `enforce` 稳定后启用
- 数据库迁移：启动时迁移 `1` 至 `19` 校验全部通过，包括目录、认证、Velocity、心跳、管理员 Web、赫朝账号、诊断上传、服务器排期、单服规则、账号安全、论坛撤销 outbox、LuckPerms 等级命令、客户端发布通道、运行遥测、服务器运行样本、统一告警与基础设施角色
- 赫朝账号：注册、登录、刷新轮换、重放拒绝、退出撤销、全部设备退出、错误密码解除拒绝、正确密码解除身份和无效 Minecraft 凭据拒绝已完成生产隔离验证；测试数据已清理
- LuckPerms 快照：114 人、4 个等级映射；内部同步无凭据返回 401
- Velocity 内部授权：无凭据和错误凭据均返回 401；有效凭据与未绑定测试 UUID 返回 `PlayerNotLinked`
- 状态心跳：错误凭据返回 401；五个目标由 `owl5` 四目标与代表恐怖整蛊的历史 `owl9-pvp` 单目标分布式写入。两机采集器均为 `0.2.1`；Survival1 已按真实停服状态记录为 `Closed`，Activity 零玩家暂停不会发送旧 Tick 数值
- 运行遥测：认证批次、幂等去重、30 天留存、三窗口聚合和后台页面已部署；基础、Activity 与恐怖整蛊已有真实安装样本，仍待真实回滚、完整 Launch/GameExit 与多人样本
- 诊断上传：编号 `1e707520` 已由真实 `0.11.14` 主动确认上传；上传端、生产端与管理员下载文件均为 `707` 字节且 SHA-256 为 `1C53C309DDA3D1D9A905836E79A041EDCD4DDD03C543E0424119C876AAA6BF92`，上传授权、上传完成与管理员下载审计均存在
- 数据库应用角色：非超级用户，无建库和建角色权限
- 公网 `8.148.207.171:5433`：连接超时，符合预期
- Nginx 站点：`/etc/nginx/sites-available/hechao-launcher.conf`
- Nginx 隐私日志：`/etc/nginx/conf.d/00-hechao-privacy-log.conf` 定义格式，`/etc/nginx/snippets/hechao-privacy-access-log.conf` 由五个 server block 引用；不记录查询字符串或 Referer
- ACME-only 回滚站点：`/etc/nginx/sites-available/hechao-launcher-acme-only.conf`
- 证书安装目录：`/etc/nginx/ssl/hechao-launcher`，私钥权限 `600`
- acme.sh 下次计划续期时间：`2026-08-19T15:59:33Z`
- systemd 安全暴露评分：`3.9 OK`
- 操作手册：[`API_OPERATIONS.md`](API_OPERATIONS.md)
- 认证手册：[`AUTHENTICATION_OPERATIONS.md`](AUTHENTICATION_OPERATIONS.md)
- 数据库手册：[`DATABASE_OPERATIONS.md`](DATABASE_OPERATIONS.md)
- Velocity 授权手册：[`VELOCITY_AUTHORIZATION_OPERATIONS.md`](VELOCITY_AUTHORIZATION_OPERATIONS.md)
- 状态心跳手册：[`SERVER_HEARTBEAT_OPERATIONS.md`](SERVER_HEARTBEAT_OPERATIONS.md)

## 6. 部署保护规则

- 新 API 先监听 `127.0.0.1:8090`，通过本机健康检查后再增加独立子域名。
- 不修改现有网站和 Sub2API 的监听地址，不安装第二个争抢 `80/443` 的代理。
- 公网只由 Nginx 终止 TLS；高位上游端口不加入 UFW 公网规则。
- 每次改 Nginx 前先保存配置并执行 `nginx -t`，失败时不得 reload。
- 日常部署不得无理由启动、停止或重启 Minecraft 服务；需要维护时只操作明确目标，并先核对玩家连接、备份与回滚路径。

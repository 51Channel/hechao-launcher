# 赫朝平台无密码资产清单

> 核查时间：2026-07-27（Asia/Shanghai）
> 用途：`PLATFORM_PLAN.md` 阶段 0 基线、部署前检查与回滚定位
> 安全边界：本文不记录密码、私钥、数据库口令、令牌或云 AccessKey

## 1. 阿里云分发与网站主机

| 项目 | 当前值 | 证据状态 |
| --- | --- | --- |
| 公网地址 | `8.148.207.171` | 已从 DNS 与主机双向核对 |
| 系统 | Ubuntu 24.04.4 LTS，x86-64，KVM | 已实时读取 |
| CPU | 2 vCPU，Intel Xeon Platinum | 已实时读取 |
| 内存 | 约 3.42 GiB 可见内存，2 GiB Swap | 已实时读取 |
| 系统盘 | 约 48.85 GiB，剩余约 38.47 GiB | 已实时读取 |
| 入站防火墙 | UFW 仅允许 TCP `22`、`80`、`443` | 已实时读取 |
| 公网带宽 | 控制台此前显示 200 Mbps 峰值 | 待在控制台复核计费规格 |

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
| `127.0.0.1:8090` | `hechao-launcher-api.service` | 启动器 API `0.20.0`、赫朝账号、账号安全、论坛会话联动、受控全局等级、六份生产档案、诊断上传、服务器排期、单服规则、授权定向路由、运行遥测、服务器深度指标与统一告警 |

Nginx 当前将 `hechao.world` 根路径转发到 `127.0.0.1:3000`，并保留若干中转 API 路径到 `127.0.0.1:8080`；`api.hechao.world` 全站转发到 `127.0.0.1:8080`。新启动器 API 不得占用这两个现有上游端口或覆盖现有 server block。

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
- 活动档案 `1.0.10` 发布前数据库备份 `/var/backups/hechao-launcher/database/hechao-launcher-20260724T120517Z.dump` 为 `64,196` 字节，SHA-256 为 `5CDF0991013A99A74622BFF23C37C9EC9C999418BB023306F18C33F9987F74A8`；发布快照目录为 `/var/backups/hechao-launcher/profile-publications/pre-activity-neoforge-1.0.10-20260724T120517Z`，其中清单归档 SHA-256 为 `5C918781D08434FC581E0F69E91ABF08F5A2E3F2756F3FC985606D51F45F9ACE`。数据库校验和与 `pg_restore --list` 均通过。
- PVP 档案 `1.0.0` 发布前数据库备份为 `/var/backups/hechao-launcher/database/hechao-launcher-20260725T202241Z.dump`；发布快照目录为 `/var/backups/hechao-launcher/profile-publications/pre-pvp-fabric-1.0.0-20260725T202252Z`，清单归档 SHA-256 为 `cd99bb5059b58ea834b0bff8d3a27d061c32439ed5e7d9e079eca21dc4cbcf0f`。
- Vanilla、Forge 与 DollNight 发布前统一备份目录为 `/var/backups/hechao-launcher/profile-publication/20260726T182024Z`；数据库备份 SHA-256 为 `183CA211FA431656FCD982305ACEA1C7859579D6D0BA9DB2511F947C37117334`，清单归档 SHA-256 为 `3A5380329322D61BAC16E8FBCB84C6036D90E44E5399A95DEA3B077AA613D884`。校验和与 `pg_restore --list` 均通过。
- 启动器 `0.11.6` 小范围测试前数据库基线 `/var/backups/hechao-launcher/database/hechao-launcher-20260726T084308Z.dump` 为 `85,620` 字节，SHA-256 为 `199e8811da08e9f9c2f1db88866f9dd51574ab9b043b6ba147b3092ad0413c36`；备份服务结果、校验和与 `pg_restore --list` 均通过。
- `hechao-launcher-db-backup.timer` 已启用，每日生成 PostgreSQL custom-format 备份并保留 14 天；首份备份校验通过，`pg_restore --list` 可读取 36 个对象。
- 首份备份已恢复到唯一命名的临时验证库，迁移版本、3 个客户端档案、4 个服务器和 0 个初始用户均与生产库一致，验证后只删除了临时库。
- 网站与 Sub2API 仍缺少新的统一每日备份和异地副本；启动器数据库的 RSA/AES-GCM 异地加密、私有 OSS 往返复验、失败告警和隔离恢复工具已部署，首次真实 OSS 往返等待 RAM v4 保存。
- 阿里云控制台快照状态尚未通过 API 或控制台复核。

### 1.4 客户端签名与发布资产

- 生产签名 Key ID 为 `release-2026-07-primary`，算法为 `ECDSA_P256_SHA256`，公钥 SHA-256 为 `6D4ACA1E787CFEDA1C3A5D7B772FB1F0E03C298848538D272B12BCFAF1C94F9E`。
- 私钥主副本由 Windows DPAPI `CurrentUser` 加密保存在 `%LocalAppData%\HechaoLauncherAdmin\secrets`，同一密文镜像位于 `H:\Hechao-SecureBackup`；两处 ACL 均仅允许当前管理员与 `SYSTEM`。
- 私钥未进入源码、发布包、API 或游戏 VPS；临时 PEM 与恢复演练的二进制 PKCS#8 已清理。发布器 `0.9.0` 已把生产私钥导出为 RSA/AES-GCM 加密恢复包并完成解密、临时 DPAPI 恢复、真实清单签名和生产公钥验签。加密恢复包等待写入私有 OSS 恢复前缀，恢复口令副本已与密文分离保存在游戏 VPS。
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
- API `0.11.1-20260725T165050Z` 基于提交 `3f17cba`。单文件为 `103,711,283` 字节，SHA-256 为 `0336CBE79E02F2E9F7F7C37490120FAA840CF083C84B02537ACFEA5266B75F45`；归档为 `45,309,559` 字节，SHA-256 为 `E727E9B840E81CDEFE5D45586AEF874B6E082D29562F797F45ECC8C98589E587`。生产健康、数据库、旧网站、中转 API、管理员入口和 journal 回归均通过，现保留为历史版本。
- API `0.12.0-20260725T203001Z` 单文件为 `103,716,915` 字节，SHA-256 为 `B46A22280243BA9801EB66FD628ED598CD27F0FED7995788C4452D222C3B27D1`；归档为 `45,382,027` 字节，SHA-256 为 `C76DA133466A4D609F8009A5206FDAFCDDE72DC0CB7D78FBC8E8C8B473DA5D41`。授权定向路由、健康/就绪、旧业务与生产合成授权回归通过，现保留为历史版本。
- API `0.13.0-20260726T173536Z` 单文件为 `103,796,275` 字节，SHA-256 为 `F2B7466A9AFAB142F110D7C2EB692DE1BA2FDD653F7CF42D4AE31D5BF7E8C811`；归档为 `45,339,427` 字节，SHA-256 为 `E7C8DECAFD8A3B47EB63987F8542C8BB034AB86C831F32B242F741FE26ABC728`。诊断上传、清理、生产错误路径和旧业务回归通过，现保留为历史版本。
- API `0.14.1-20260726T190856Z` 单文件为 `103,854,131` 字节，SHA-256 为 `F02CC7AAC3AE4FC8726548E3777D231D035B03E19487CAB32627333CEBBB8A3A`；归档为 `45,365,349` 字节，SHA-256 为 `877C9EBE6CDB5B611E0495F69BC759D6D51B4A1A1AF8D60A906F7BEC57E8959E`。排期、公告、玩家搜索、访问预览、单服规则、迁移 10、独立端口启动预检和公网回归通过，现保留为历史版本。
- API `0.15.0-20260726T202540Z` 单文件为 `103,950,899` 字节，SHA-256 为 `42ACC44468989A567E936993934046266A9D2B22B43758322E693BC23A089FD6`；归档为 `45,404,470` 字节，SHA-256 为 `9B096CBB55636494D64148908DA7168D5B748E12086BE6C854417891FEBBF10A`。账号安全、迁移 11、生产备份还原后的隔离端到端验收、原子部署和公网回归通过，现为 `0.16.0` 的直接回滚目标。
- API `0.16.0-20260726T222124Z` 单文件为 `104,046,643` 字节，SHA-256 为 `8B932BC0BFE5C0D3D2A97460555695AD33544CC2814A96B8AF16E672F8B5CDB5`；归档为 `45,433,983` 字节，SHA-256 为 `2B1568D5A72DAE09E0CED270633099AA961F11EBC730490451E1448BDA9EE4D4`。论坛 Cookie 联动、受控全局等级、迁移 12 至 13、生产 worker 投递、原子部署和公网回归通过，现为历史版本。
- API `0.19.0-20260727T005013Z` 基于提交 `7ba2eba`。单文件为 `104,346,675` 字节，SHA-256 为 `29B351C33B6366BF2C3E9263275928D0F5C8329D05C14B1C7A138C0D81B279FA`；无 PDB 归档为 `45,550,337` 字节，SHA-256 为 `B8A82819AB0CD42F09A1B435A29CFFC26C6335215157EB8FF5FE1F48B9755455`。迁移 16、服务器运行样本、服务状态后台、隔离生产副本、原子部署和公网回归通过，当前在线。
- API `0.20.0-20260727T011953Z` 基于提交 `b5c1a78`。单文件为 `104,441,907` 字节，SHA-256 为 `67C3E084D9E53509B283A4B39498219C33BF1676BB4F1805A916E83CFFABBDEB`；无 PDB 归档为 `45,575,077` 字节，SHA-256 为 `874C05ECE9AA6DE628C1B7E99191D8E7FB50745E7224059D1F5D8C90E95A8665`。迁移 17、请求指标、统一告警、后台告警页、平台监控器、隔离生产副本、原子部署和公网回归通过，当前在线。
- 正式基础档案为 `base-1.21.11` / `1.0.5`，清单 SHA-256 为 `65667E6198C3ECF75DF79C686C87C244F3D5AC21B170364BD998A1DF5111640E`；包含 `4,902` 个文件、`4,900` 个去重对象和 `874,147,856` 字节。
- NeoForge 活动正式档案为 `activity-neoforge-1.21.11` / `1.0.10`，清单 SHA-256 为 `0E059BBFE9FAB6770204DE547567CA64420A45E8364FA93206BB316E8AE2B69F`；包含 `4,754` 个文件与对象、`621,732,083` 字节。Meccha SHA-256 `C72511BEF3B0CC2C1A1C97E1C33709901714460191F9549FD461E71215534E9E` 与活动服一致；生产信任验签、发布物闭合验收、全量安装、逐文件复验、NeoForge `21.11.42` 进程构建、分级授权和真实 OSS 下载均通过，当前解决方案测试为 `157/157`。API 已无重启原子激活，活动服保持 `Closed 0/30`。
- PVP 正式档案为 `pvp-fabric-1.20.1` / `1.0.0`，清单 SHA-256 为 `A5BCBBA71C69E85F0ACE4000C1983F8C9C1C1D7F546AFA36C53AE39C895706E6`；包含 `3,749` 个逻辑文件、`3,748` 个去重对象和 `885,821,291` 字节。加载器为 Fabric `0.16.14`，运行时为 Java `17`；生产上传新增 `3,547` 个对象、校验跳过 `201` 个对象，当前目录显示名为“恐怖整蛊”。
- Vanilla 正式档案为 `vanilla-1.21.11` / `1.0.0`，清单 SHA-256 为 `C22DEDC09576273B6D4C52B07CF7975D09BA758533B7395974BE34F73344C865`；包含 `4,671` 个文件与对象、`549,101,696` 字节，运行时为 Java `21`，已绑定 Survival1。
- Forge 正式档案为 `forge-1.20.1` / `1.0.0`，清单 SHA-256 为 `D33FF592B115667713BCC87477710AA7D8A86F77490C23B70B7DEE620A56919C`；包含 `3,667` 个文件与对象、`725,771,107` 字节，加载器为 Forge `47.4.0`，运行时为 Java `17`。当前未绑定服务器，因此不会出现在玩家目录。
- DollNight 正式档案为 `dollnight-1.21.11` / `1.0.0`，清单 SHA-256 为 `6D0C73C2B8CD34621C5D44212047DC562AD05E8277B1F195BDAC0FDA5DA16575`；包含 `4,902` 个逻辑文件、`4,900` 个去重对象和 `874,147,856` 字节，加载器为 Fabric `0.19.2`，运行时为 Java `21`，已绑定 DollNight。三份新增档案的生产信任验签、闭合校验、全量安装、逐文件复验、进程构建、权限和对象下载回归均通过；当前解决方案测试为 `251/251`。
- 独立发布 RAM 用户 `hechao-launcher-publisher` 当前绑定 `HechaoLauncherOssObjectPublish` v3，仅可对 `hechaoworld/objects/*` 与 `hechaoworld/releases/launcher/*` 执行 `oss:GetObject` 与 `oss:PutObject`；RAM v4 模板只额外加入 `hechaoworld/backups/database/*` 与 `hechaoworld/backups/recovery/*`，等待控制台保存。没有列举、其他前缀读取、删除或版本管理权限。AccessKey 仅以 Windows DPAPI `CurrentUser` 密文保存在管理员电脑，并以 `/etc/hechao-offsite-backup/environment` 的 root-only 独立环境提供给异地备份服务。API 继续使用 `hechao-launcher-distribution` 只读身份；两份生产 AccessKey ID 哈希均已与本机受保护凭据匹配，未重启 API。首批基础档案 `4,900` 个对象共 `874,147,706` 字节；活动档案随后提交 `4,754` 个对象和 `621,732,083` 字节；启动器 `0.10.0` 私有安装包为 `61,796,065` 字节。
- Bucket 版本控制已开启，OSS 会忽略 `x-oss-forbid-overwrite`。活动首次上传因此为 `4,551` 个共享摘要创建同内容新版本，真正新增 `203` 个摘要、`152,843,997` 字节。发布器 `0.7.0` 已改为先校验当前对象长度与 SHA-256 元数据，匹配则跳过，不匹配则硬失败；生产全量复验结果为 `0` 个上传、`4,754` 个跳过、`0` 上传字节。完整记录见 [`ACTIVITY_PROFILE_RELEASE_1.0.10.md`](ACTIVITY_PROFILE_RELEASE_1.0.10.md)。

## 2. 主 Minecraft VPS：owl5

| 项目 | 当前值 | 证据状态 |
| --- | --- | --- |
| 系统 | Windows Server 2022 Standard | 已实时读取 |
| CPU 配额 | 6 核 / 6 逻辑处理器，宿主型号 Ryzen 9 9950X | 已实时读取 |
| 内存 | 约 18.0 GiB | 已实时读取 |
| `C:` | 约 39.13 GiB，剩余 `9,402,572,800` 字节（约 8.76 GiB） | 迁移一份历史备份后实时读取 |
| `E:` | 约 69.99 GiB，剩余 `14,922,022,912` 字节（约 13.90 GiB） | 清理损坏并发备份、迁移历史备份后实时读取 |
| SSH | 外部端口 `15152`，Windows 内部 `22` | 已验证密钥登录 |
| RDP | 外部端口 `15153`，Windows 内部 `3389` | 连接方式已记录，未在本轮登录 |

### 2.1 Velocity 与后端目录

| 逻辑服务 | 目录 | 内部端口 | 核查时状态 |
| --- | --- | --- | --- |
| Velocity | `E:\Velocity` | `25577` | 运行中，`-Xmx1G` |
| 大厅 | `E:\LobbyServer` | `25566` | 运行中，`-Xmx2G` |
| Survival1 | `E:\Survival1` | `19228` | 运行中，`-Xmx2G` |
| Survival2 | `E:\Survival2` | `25565` | 运行中，`-Xmx2G` |
| Activity NeoForge | `E:\ActivityNeoForge` | `25568` | 未监听 |
| DollNight | `E:\DollNight` | 与 Survival2 共用 `25565` | 替换服，不可与 Survival2 同时运行 |

Velocity 路由基线：`lobby -> 127.0.0.1:25566`、`survival1 -> 127.0.0.1:19228`、`survival2 -> 127.0.0.1:25565`、`activity -> 127.0.0.1:25568`、`pvp -> owl9.vipi9.top:19243`。公网入口保持 `mc.hehe11.fun`，Minecraft SRV 记录指向外部端口 `15156`；2026-07-26 重启后从外网验证该端口 TCP 可达，默认 `25565` 不直接开放。

保留替换服目录：`E:\ActivityHybrid`、`E:\ActivityLocal`、`E:\ActivityServer`、`E:\MonsterActivity`。这些目录不能与占用相同入口端口的当前活动后端同时启动。

Velocity 授权插件 `HechaoVelocityAuthorizer-0.2.0.jar` 已放入 `E:\Velocity\plugins`，SHA-256 为 `9CBBB1453D7260CD8AAD48EDC6BE4E80B8A5E41374D5012E0DBA64ACC0188D37`；配置位于 `E:\Velocity\plugins\hechao-velocity-authorizer\config.properties`，模式为 `monitor`，ACL 已收紧，备份位于 `E:\manual-backups\VelocityAuthorizer-0.2.0-20260726-044028`。计划任务 `Codex-Velocity-Live` 当前 PID 为 `6068`；启动日志确认插件 `0.2.0` 已加载并为 `owl5-main` 初始化。

本次激活前代理没有已建立的玩家连接；重启只作用于 Velocity，大厅、生存服、活动服和其他 Java 进程均未被操作。

LuckPerms 使用各 Paper 服共享的本机 MariaDB；启动器同步桥位于 `C:\ProgramData\Hechao\LauncherBridge`，计划任务 `Hechao Launcher LuckPerms Sync` 以 `SYSTEM` 身份每 5 分钟只读同步。当前快照共 114 人：`default=99`、`vip=12`、`admin=1`、`owner=2`。同步任务不控制任何 Minecraft 进程。

受控等级代理 `E:\LobbyServer\plugins\HechaoLuckPermsTierAgent-0.1.0.jar` 的
SHA-256 为 `35A9BBB17620DC2FD7245E0EA8CCAA293DC98C264DA3463AB706846ED7E42A7B`；
配置位于同目录的 `HechaoLuckPermsTierAgent\config.properties`，ACL 已收紧，备份位于
`E:\manual-backups\luckperms-tier-agent-20260726T223127Z`。安装前后 Java PID
不变且没有重启大厅；插件等待服主下次自行重启后加载。

状态采集器 `0.2.0` 位于 `C:\ProgramData\Hechao\StatusCollector`，单文件 EXE SHA-256 为 `354186EF1D1B559D72107E80AD56467371CF7D59FCB31D5763E4C7B2B7F4A424`。计划任务 `Hechao Launcher Server Heartbeats` 以 `SYSTEM` 身份每分钟查询 `lobby`、`survival2`、`survival1`、`pvp` 和 `activity`，令牌使用 `LocalMachine` DPAPI 加密。大厅、Survival2、Survival1 已上报进程工作集、CPU、启动时间和 E 盘容量；活动服关闭、PVP 不可达时使用固定问题代码，单个目标失败不会中断其余心跳。旧采集器、配置和计划任务备份位于 `C:\ProgramData\Hechao\StatusCollector\backups\collector-0.2.0-20260727T004750Z`。采集器不包含 RCON 或进程启停能力。

Paper/Purpur 指标代理 `HechaoServerMetrics-0.1.0.jar` 已复制到大厅、Survival2、
Survival1 的 `plugins` 目录，三份 SHA-256 均为
`BD03312007E043223B37CF634872C3DAA4C0FB11B80B54ADC546507853528B2C`，备份位于
`E:\manual-backups\server-metrics-20260727T004852Z`。部署前后 Java PID 未改变，
没有重启服务端；TPS/MSPT/GC 等待服主下次自行重启后加载。

2026-07-26 曾因两个每日任务并发创建约 3.4 GB 的不完整 ZIP，导致 `E:` 空间耗尽。两个损坏归档和一个 0 字节大厅归档已验证后清理；一份 `7,963,944,183` 字节的历史备份在核对文件名与大小后迁移到 `C:\manual-backups\E-drive-overflow`。新的世界备份引擎位于 `C:\ProgramData\Hechao\WorldBackup\Invoke-WorldBackup.ps1`，大小 `9,900` 字节，SHA-256 为 `C8166E8DE97AB3CCC03B6C652266C2B4541CA05F66E0FA271366C0845F9F1DB8`，会全局串行、按接近源文件总量做磁盘预检、写入 `.partial`、校验 ZIP 条目，并让 ZIP 与 SHA-256 旁车文件成对完成。远端双轮冒烟测试已通过；首次由 Essentials 正式计划任务生成的世界归档仍待验收。

同日复核的世界源文件量为 Survival1 `6,401,231,920` 字节、Survival2
`10,852,061,168` 字节、Lobby `11,556,833` 字节。磁盘上的计划已调整为
Survival2 02:00、Survival1 04:00、Lobby 05:30，Lobby Essentials 的旧
30 分钟循环已设为 0；变更前文件保存在
`E:\manual-backups\world-backup-schedule-20260726T062655Z`。本次没有热重载、
停止或重启服务，三个 Paper 后端与 Velocity 的监听 PID 均保持不变。

两个不参与运行的旧迁移 ZIP 共 `7,388,961,944` 字节已转存到管理机
`H:\server-backups\owl5`。两份本地文件均按 VPS 原件重新验证大小和 SHA-256，
并保存 `.sha256` 旁车文件后才删除远端原件。`E:` 当前可用
`22,310,768,640` 字节，约 20.78 GiB；三服最坏空间门槛约 17.39 GiB，
额外余量约 3.39 GiB。

## 3. 旧 Minecraft VPS：owl9

最后已知信息：Windows 主机、SSH 外部端口 `19241`、RDP `19242`、Minecraft 外部端口 `19243`、服务根目录 `C:\mc\server`、Fabric 1.21.11。

2026-07-21 使用当前运维密钥验证失败，因此以上信息只能作为历史记录，不能作为上线依据。需要重新导入公钥或由管理员提供当前连接方式后再完成实时盘点。

## 4. 当前阻塞与风险

1. `download.hechao.world` 的 CNAME、HTTPS、私有 Bucket、读写分离 RAM 身份、真实客户端对象、签名清单和生产签名信任链已完成；生产签名离线恢复演练已通过，加密恢复包等待 RAM v4 保存后写入私有 OSS。
2. `owl5` 的 `E:` 已恢复到约 20.78 GiB，最坏预检余量约 3.39 GiB；首次正式计划备份仍必须核对 ZIP、SHA-256、保留数量和剩余空间。
3. `owl9` 当前无法通过密钥认证，第二台 VPS 的实时规格与服务状态未完成。
4. 启动器数据库异地加密、上传/下载复验、告警和隔离恢复工具已部署，等待 RAM v4 保存后做首次真实往返；网站与 Sub2API 仍没有新的统一异地备份和恢复演练。
5. Microsoft 公共客户端已注册，Minecraft Java API 许可已由管理员确认通过；Velocity `0.2.0` 已加载为 `monitor`，全部目标映射和合成定向路由已通过。真实四级账号、NPC 转服、`/hub`、断线重连和 API 故障路径仍待灰度，因此 `enforce` 与目录强制登录开关尚未启用。
6. 当前 `Hechao.Launcher.exe` 按已确认决策保持 `NotSigned`，Windows SmartScreen 首次运行提示属于已接受的首版发布风险；正式公告必须提供官方来源、大小和 SHA-256。客户端清单的 ECDSA 签名不能替代 EXE 代码签名，未来若增加 Authenticode 必须独立升版。

## 5. 当前 API 部署状态

- 发布 ID：`0.20.0-20260727T011953Z`
- API `0.20.0` 与平台监控器 `0.1.0` 已部署；启动器 `0.11.12` 为私有 OSS 灰度版本，`0.11.13` 为遥测源码候选。管理员 Web 已启用，但当前 MFA 凭据数为 `0`
- Git 标签：`launcher-v0.11.12` 指向 `2e21c5b93980bb62120e0f5cb9ee124658966d20`；API、Velocity 与各档案标签按 [`RELEASE_AND_GIT_WORKFLOW.md`](RELEASE_AND_GIT_WORKFLOW.md) 管理
- 运行账户：`hechao-api`，无交互登录权限
- systemd：已启用并通过重启恢复测试
- 监听：仅 `127.0.0.1:8090`
- 公网 `8.148.207.171:8090`：连接超时，符合预期
- 公网入口：`https://launcher-api.hechao.world`
- `healthz`、数据库感知的 `readyz`：本机 HTTP 与公网 HTTPS 均为 200
- `GET /v1/catalog`：过渡阶段匿名请求返回目录，无效 Bearer 返回 401；正式强制开关待认证许可完成后启用
- 数据库迁移：启动时迁移 `1` 至 `17` 校验全部通过，包括目录、认证、Velocity、心跳、管理员 Web、赫朝账号、诊断上传、服务器排期、单服规则、账号安全、论坛撤销 outbox、LuckPerms 等级命令、客户端发布通道、运行遥测、服务器运行样本与统一告警
- 赫朝账号：注册、登录、刷新轮换、重放拒绝、退出撤销、全部设备退出、错误密码解除拒绝、正确密码解除身份和无效 Minecraft 凭据拒绝已完成生产隔离验证；测试数据已清理
- LuckPerms 快照：114 人、4 个等级映射；内部同步无凭据返回 401
- Velocity 内部授权：无凭据和错误凭据均返回 401；有效凭据与未绑定测试 UUID 返回 `PlayerNotLinked`
- 状态心跳：错误凭据返回 401；真实五目标批次成功写入，活动服离线被隔离，目录实时人数与维护状态覆盖通过
- 运行遥测：认证批次、幂等去重、30 天留存、三窗口聚合和后台页面已部署；真实启动器 `0.11.13` 与管理员 MFA 页面待验收
- 数据库应用角色：非超级用户，无建库和建角色权限
- 公网 `8.148.207.171:5433`：连接超时，符合预期
- Nginx 站点：`/etc/nginx/sites-available/hechao-launcher.conf`
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

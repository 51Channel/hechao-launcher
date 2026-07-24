# 赫朝平台无密码资产清单

> 核查时间：2026-07-24（Asia/Shanghai）
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
| `127.0.0.1:8090` | `hechao-launcher-api.service` | 启动器 API `0.9.0`、赫朝账号、真实目录、认证会话、签名分发、Velocity 授权与状态心跳 |

Nginx 当前将 `hechao.world` 根路径转发到 `127.0.0.1:3000`，并保留若干中转 API 路径到 `127.0.0.1:8080`；`api.hechao.world` 全站转发到 `127.0.0.1:8080`。新启动器 API 不得占用这两个现有上游端口或覆盖现有 server block。

### 1.2 域名与证书基线

| 域名 | 当前状态 |
| --- | --- |
| `hechao.world` | A -> `8.148.207.171`，TTL 约 600 秒，HTTPS 200 |
| `api.hechao.world` | A -> `8.148.207.171`，TTL 约 600 秒，HTTPS 200 |
| `launcher-api.hechao.world` | A -> `8.148.207.171`，HTTPS `/healthz` 与 `/readyz` 均为 200 |
| `admin.hechao.world` | A -> `8.148.207.171`，TLS 有效；后台应用未部署前按设计返回 404 |
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
- `hechao-launcher-db-backup.timer` 已启用，每日生成 PostgreSQL custom-format 备份并保留 14 天；首份备份校验通过，`pg_restore --list` 可读取 36 个对象。
- 首份备份已恢复到唯一命名的临时验证库，迁移版本、3 个客户端档案、4 个服务器和 0 个初始用户均与生产库一致，验证后只删除了临时库。
- 网站与 Sub2API 仍缺少新的统一每日备份和异地副本；启动器数据库已完成同主机隔离恢复演练，但仍缺异地副本。
- 阿里云控制台快照状态尚未通过 API 或控制台复核。

### 1.4 客户端签名与发布资产

- 生产签名 Key ID 为 `release-2026-07-primary`，算法为 `ECDSA_P256_SHA256`，公钥 SHA-256 为 `6D4ACA1E787CFEDA1C3A5D7B772FB1F0E03C298848538D272B12BCFAF1C94F9E`。
- 私钥主副本由 Windows DPAPI `CurrentUser` 加密保存在 `%LocalAppData%\HechaoLauncherAdmin\secrets`，同一密文镜像位于 `H:\Hechao-SecureBackup`；两处 ACL 均仅允许当前管理员与 `SYSTEM`。
- 私钥未进入源码、发布包、API、OSS 或游戏 VPS；临时 PEM 已清理。`H:` 镜像依赖同一 Windows 用户配置，尚不能作为离机灾难恢复副本。
- `0.6.0` 启动器已嵌入该公钥并完成程序集资源加载、真实清单签名/验签、篡改拒绝、全量安装、受管 Java 21、Fabric 进程构建和启动前授权验证；加入状态采集后解决方案测试为 `80/80` 通过。
- `Hechao-Launcher-0.6.0-win-x64.zip` SHA-256 为 `9529C175A168EDE850D4A519E50EA71268BB8A809D128FC5076F18D48D90CC0C`，其中单文件 EXE SHA-256 为 `0DF28FD71DA34303C1FAAC11C1D041884C4AF664D192D3D2A719FAF9A602C2E7`；`Hechao-Publisher-0.5.0-win-x64.zip` SHA-256 为 `176EAF4B50C36A9254E90C8B3EB5F35FAC4089095C594B3A94932B395F46B696`。
- 启动器 `0.8.0` 最终本机候选为 `68,511,931` 字节，EXE SHA-256 为 `CB21C2A860DFDE961C495281BBD58AAC62ECB064C8E3A6B7B098F7EAF7DB54EC`；它已完成五工作区与短屏响应式检查，但尚未上传或向玩家分发。
- 启动器 `0.8.1` 本机候选为 `68,547,259` 字节，EXE SHA-256 为 `C2B3F5D720793FE18EBDBD71336F45F55498C1F074BB5831C86F1236FB55956D`；它使用 IconPark 官方轮廓图标并优先采用系统苹方字体，五个工作区实机检查与 `125/125` 解决方案测试均已通过，IconPark 授权文件同时内嵌并随发布目录提供，尚未上传或向玩家分发。
- 启动器 `0.8.2` 本机候选为 `68,547,782` 字节，EXE SHA-256 为 `53D27A7F51FFFCB72315C02CDEA751A33FC39E18D3F089C01A40C4097EDC04BD`；它放大了全局界面文字，修正了客户端准备步骤线与运行配置行的对齐，并针对 125% DPI 将苹方渲染改为 Ideal 字体度量、ClearType 抗锯齿和 Fixed 静态提示。五个工作区实机检查与 `125/125` 解决方案测试均已通过，尚未上传或向玩家分发。
- 启动器 `0.9.0` 已改为正式安装式候选，程序目录与游戏数据分离；每个档案使用独立 `.minecraft`，共享对象和受管 Java，并支持从旧 `%AppData%` 或自定义根目录迁移。单文件 EXE 为 `68,556,366` 字节，SHA-256 为 `73347225BDDFF2A0F43DB57F13B4CF41AB1BEE1B46FC0CD74A992647DE9496E2`；NSIS 安装包为 `61,782,139` 字节，SHA-256 为 `35240A3A21764A21ACB286FC30B1FC4755DE90844B49B075FB8B69174018B97C`。Windows 安装/卸载冒烟测试和 `135/135` 解决方案测试已通过；两份 EXE 当前均为 `NotSigned`，尚未上传或向玩家分发。
- 正式基础档案为 `base-1.21.11` / `1.0.5`，清单 SHA-256 为 `65667E6198C3ECF75DF79C686C87C244F3D5AC21B170364BD998A1DF5111640E`；包含 `4,902` 个文件、`4,900` 个去重对象和 `874,147,856` 字节。
- 独立发布 RAM 用户 `hechao-launcher-publisher` 仅具备 `HechaoLauncherOssObjectPublish` 的 `oss:PutObject` 权限；AccessKey 仅以 Windows DPAPI `CurrentUser` 密文保存，主副本位于 `%LocalAppData%\HechaoLauncherAdmin\secrets`，同密文镜像位于 `H:\Hechao-SecureBackup`。首批 `4,900` 个对象已上传 OSS，共 `874,147,706` 字节，未覆盖任何既有对象。

## 2. 主 Minecraft VPS：owl5

| 项目 | 当前值 | 证据状态 |
| --- | --- | --- |
| 系统 | Windows Server 2022 Standard | 已实时读取 |
| CPU 配额 | 6 核 / 6 逻辑处理器，宿主型号 Ryzen 9 9950X | 已实时读取 |
| 内存 | 约 18.0 GiB | 已实时读取 |
| `C:` | 约 39.13 GiB，剩余约 16.22 GiB | 已实时读取 |
| `E:` | 约 69.99 GiB，剩余约 6.48 GiB | 已实时读取，空间偏低 |
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

Velocity 路由基线：`lobby -> 127.0.0.1:25566`、`survival1 -> 127.0.0.1:19228`、`survival2 -> 127.0.0.1:25565`、`activity -> 127.0.0.1:25568`。

保留替换服目录：`E:\ActivityHybrid`、`E:\ActivityLocal`、`E:\ActivityServer`、`E:\MonsterActivity`。这些目录不能与占用相同入口端口的当前活动后端同时启动。

Velocity 授权插件 `HechaoVelocityAuthorizer-0.1.0.jar` 已放入 `E:\Velocity\plugins`，SHA-256 为 `BA1E02150714A34D5FCEA348C64C578B31D9E4C85B53D3DA8EFD3681F31388C4`；配置位于 `E:\Velocity\plugins\hechao-velocity-authorizer\config.properties`，模式为 `monitor`，ACL 已收紧，备份位于 `E:\manual-backups\velocity-authorizer-20260723T103346Z`。安装前后 `25577` 的监听 PID 均为 `10324`，因此插件尚未被当前进程加载，将在管理员下一次手动重启 Velocity 后生效。

本轮仅执行了文件安装、系统与端口读取，没有启动、停止或重启任何 Minecraft 进程。

LuckPerms 使用各 Paper 服共享的本机 MariaDB；启动器同步桥位于 `C:\ProgramData\Hechao\LauncherBridge`，计划任务 `Hechao Launcher LuckPerms Sync` 以 `SYSTEM` 身份每 5 分钟只读同步。当前快照共 114 人：`default=99`、`vip=12`、`admin=1`、`owner=2`。同步任务不控制任何 Minecraft 进程。

状态采集器 `0.1.0` 位于 `C:\ProgramData\Hechao\StatusCollector`，单文件 EXE SHA-256 为 `D24201646D13CF0CA4F7261DC06C0EFAF2BC9B425A9B539BD3689095EFAD8DFF`。计划任务 `Hechao Launcher Server Heartbeats` 以 `SYSTEM` 身份每分钟查询 `lobby`、`survival2` 和 `activity` 的 Minecraft 状态，令牌使用 `LocalMachine` DPAPI 加密；首次自动执行返回码为 `0`。实测大厅 `0/200`、`survival2` `0/100`、活动服端口未监听，API 目录分别返回 `Online`、`Online`、`Closed`，玩偶服仍由后台 `Maintenance` 状态覆盖。采集器不包含 RCON 或进程启停能力，HTTP 自动重定向已关闭以避免内部请求头离开配置的 API 地址。

## 3. 旧 Minecraft VPS：owl9

最后已知信息：Windows 主机、SSH 外部端口 `19241`、RDP `19242`、Minecraft 外部端口 `19243`、服务根目录 `C:\mc\server`、Fabric 1.21.11。

2026-07-21 使用当前运维密钥验证失败，因此以上信息只能作为历史记录，不能作为上线依据。需要重新导入公钥或由管理员提供当前连接方式后再完成实时盘点。

## 4. 当前阻塞与风险

1. `download.hechao.world` 的 CNAME、HTTPS、私有 Bucket、读写分离 RAM 身份、真实客户端对象、签名清单和生产签名信任链已完成；离机私钥恢复副本仍待建立。
2. `owl5` 的 `E:` 仅剩约 6.48 GiB，客户端构建物、备份或世界文件继续增长前必须清理或扩容。
3. `owl9` 当前无法通过密钥认证，第二台 VPS 的实时规格与服务状态未完成。
4. 启动器数据库已有本机每日备份和同主机恢复演练，但所有业务仍缺少异地副本，网站与 Sub2API 也没有新的统一恢复演练。
5. Microsoft 公共客户端已注册，Minecraft Java API 许可已于 2026-07-22 提交申请但尚未批准；Velocity 最终授权已实现并安装为 `monitor`，仍待管理员手动重启、目标映射核对和真实账号灰度，因此目录强制登录开关尚未启用。
6. 当前 `Hechao.Launcher.exe` 尚未配置 Authenticode 代码签名，Windows SmartScreen 首次运行提示仍是生产发布风险；客户端清单的 ECDSA 签名不能替代 EXE 代码签名。

## 5. 当前 API 部署状态

- 发布 ID：`0.9.0-20260723T195253Z`
- API `0.9.0` 已部署；启动器 `0.9.0` 为未分发安装包候选。管理员 Web 代码已进入生产二进制但功能保持关闭
- 运行账户：`hechao-api`，无交互登录权限
- systemd：已启用并通过重启恢复测试
- 监听：仅 `127.0.0.1:8090`
- 公网 `8.148.207.171:8090`：连接超时，符合预期
- 公网入口：`https://launcher-api.hechao.world`
- `healthz`、数据库感知的 `readyz`：本机 HTTP 与公网 HTTPS 均为 200
- `GET /v1/catalog`：过渡阶段匿名请求返回目录，无效 Bearer 返回 401；正式强制开关待认证许可完成后启用
- 数据库迁移：记录 `1` 至 `7` 全部通过，包括目录、认证、Velocity、心跳、管理员目录、管理员 Web 与赫朝账号
- 赫朝账号：注册、登录、刷新轮换、重放拒绝、退出撤销和无效 Minecraft 凭据拒绝已完成生产隔离验证；测试数据已清理
- LuckPerms 快照：114 人、4 个等级映射；内部同步无凭据返回 401
- Velocity 内部授权：无凭据和错误凭据均返回 401；有效凭据与未绑定测试 UUID 返回 `PlayerNotLinked`
- 状态心跳：错误凭据返回 401；真实三目标批次成功写入，目录实时人数与维护状态覆盖通过
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
- 平台施工不自动启动、停止或重启任何 Minecraft 服务端。

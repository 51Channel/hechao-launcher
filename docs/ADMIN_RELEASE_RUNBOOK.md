# 赫朝启动器管理员发布手册

> 当前生产 API：`0.32.2-20260815T105349Z`
> 当前启动器私有 OSS 发布：`0.15.8`
> 当前 Velocity 插件：`0.5.0`（`monitor`；首次故障和基础设施目标始终硬拒绝）
> 适用范围：启动器、API、客户端档案、Velocity 插件、世界备份与分发记录
> 明确边界：发布脚本不顺带启动、停止或重启 Minecraft 后端；确需维护时必须作为单独运维动作记录
> owl9 边界：历史 `pvp` 档案和路由对应恐怖整蛊服 `C:\mc\server`；真正 PVP
> 服为 `E:\MinecraftServer`，没有当前启动器档案或路由，禁止将两者作为同一目标发布。

## 1. 发布原则

每次发布必须同时具备可追溯源码、自动测试、不可变发布物、SHA-256、部署前备份、部署后回归、明确回滚目标、Git 提交和组件标签。没有完成这些条件的文件只能称为测试构建，不能覆盖已有正式版本。

当前活动发布的统一台账为
[`evidence/ACTIVE_RELEASE_PROVENANCE_2026-07-28.json`](evidence/ACTIVE_RELEASE_PROVENANCE_2026-07-28.json)。
发布前后都必须运行：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Test-ReleaseProvenanceLedger.ps1
```

校验必须确认标签为注释标签、标签落点存在、标签发布人与台账一致、构建来源可解析、
主制品 SHA-256 唯一、回滚目标非空且所有证据路径位于仓库内。

密码、私钥、AccessKey、数据库口令、Microsoft/Minecraft 令牌、赫朝会话和 VPS 凭据不得进入 Git、发布说明、命令行参数或玩家安装包。

当前 Windows 安装包按已确认决策保持 `NotSigned`。内部和小范围灰度可以继续，但公告必须说明 SmartScreen 风险，并同时发布可信来源、文件大小和 SHA-256。以后增加 Authenticode 时应作为独立版本处理，不能覆盖既有安装包。

## 2. 发布类型

| 类型 | 版本示例 | 必须验证 |
| --- | --- | --- |
| 启动器 | `launcher-v0.11.16` | 完整测试、安装/升级/卸载、登录、退出状态刷新、日志配置恢复、下载、修复、诊断上传、隐私受限遥测、启动前授权 |
| API | `api-v0.20.2` | 数据库与论坛一致性备份、独立端口端到端验收、原子部署、健康/就绪、认证、敏感 URL 日志、旧域名与公网端口 |
| 客户端档案 | `profile-pvp-fabric-1.20.1-v1.0.0` | 清单签名、全对象哈希、干净安装、加载器与 Java、回滚 |
| Velocity 插件 | `velocity-authorizer-v0.3.0` | Java 测试、配置模式、目标映射、会话来源及客户端兼容矩阵；部署和重启分开记录 |
| 状态采集器 | `status-collector-v0.2.1` | 只读状态、进程与磁盘查询、空服暂停、任务结果、令牌 ACL、无进程控制能力 |
| 世界备份引擎 | 运维提交 | 串行锁、磁盘预检、ZIP 条目校验、SHA-256、原子完成、保留策略 |

组件独立升版。只修改 API 时不要无意义地提高启动器版本；只更新活动档案时不要覆盖同版本对象或清单。

## 3. 发布前基线

1. 确认 Git 工作区干净，本地 `main` 与 `origin/main` 一致。
2. 记录当前组件版本、提交、生产发布 ID和回滚目标。
3. 检查 `hechao.world`、`api.hechao.world`、`launcher-api.hechao.world` 和 `admin.hechao.world` 当前状态。
4. 确认 API 只监听 `127.0.0.1:8090`，数据库只监听回环地址。
5. 核对 Minecraft Java API 审核、目录强制登录、Velocity 模式和管理员 Web 启用状态，不得把“代码已完成”写成“功能已激活”。
6. 确认没有计划外的 Minecraft 或 Velocity 启停操作。
7. 涉及世界备份时记录各盘剩余空间，确认没有遗留 `.partial`，并等待服务端自身保存/冻结流程触发正式备份。

基线异常时先停止发布。不能通过重启全部服务、关闭数据库或临时开放公网高位端口掩盖问题。

## 4. 构建与测试

启动器正式候选统一执行：

```powershell
dotnet test Hechao.Launcher.sln -c Release
.\tools\Build-WindowsInstaller.ps1 -SkipTests
.\tools\Test-ReleaseProvenanceLedger.ps1
git diff --check
```

记录：

- 提交 ID、产品版本和构建时间。
- 测试总数、失败数和跳过数。
- 安装包与安装后 EXE 的文件大小和 SHA-256。
- Windows 签名状态；当前预期为 `NotSigned`。
- 隔离目录安装、覆盖升级和卸载结果。
- 游戏数据、设置、世界和诊断目录是否保持不变。

API 使用 `linux-x64` 自包含单文件发布。发布目录不得包含 PDB、环境文件或任何秘密。归档前核对程序集版本，归档后再次计算 SHA-256。

## 5. 客户端档案发布

1. 从独立干净源目录构建，不直接把玩家日常 `.minecraft` 当发布源。
2. 排除账号缓存、日志、崩溃转储、世界、截图、私钥、令牌和重解析点。
3. 使用离线生产私钥生成签名清单和内容寻址对象。
4. 使用公钥信任包独立验签，再运行发布器 `validate-release`，逐个重算对象大小和 SHA-256，并拒绝清单缺失、多余旧对象或 URL 哈希不一致。
5. 使用发布器 `0.7.0` 或更高版本上传。它必须先通过 `HeadObject` 校验当前对象的 `Content-Length` 与 `x-oss-meta-sha256`：匹配则跳过，不匹配则硬失败，仅缺失时上传并再次校验。OSS 在版本控制已开启或暂停时会忽略 `x-oss-forbid-overwrite`，不能只依赖该请求头。
6. 先发布隐藏测试档案或内部测试服务器，完成全新安装和修复。
7. 记录档案 ID、版本、Minecraft、加载器、Java、文件数、对象数、逻辑大小和清单 SHA-256。
8. 从全部活动签名清单重建内容寻址对象恢复集，完成本地全量哈希、独立主机安装、
   远端全量哈希和隔离恢复，再原子更新 `current`。不得只追加本次新增对象；具体见
   [`DISTRIBUTION_OBJECT_RECOVERY.md`](DISTRIBUTION_OBJECT_RECOVERY.md)。
9. 验收后再原子更新生产清单与目录记录。

活动档案必须使用独立 `profile-id`。Forge、Fabric、NeoForge 和原版档案不得共享可写 `.minecraft`。

恐怖整蛊档案 `pvp-fabric-1.20.1` 使用 Java 17 与 Fabric `0.16.14`；基础和 NeoForge 1.21.11 档案使用 Java 21。发布记录必须保留这个运行时差异，不能因为客户端均从同一启动器进入而共用可写游戏目录。

NeoForge 活动档案先运行 `tools/Prepare-NeoForgeActivityProfile.ps1`。该工具只接受不存在的输出目录，强制校验服务端同款 Meccha，并排除日志、存档、截图、账号缓存、PCL 运行文件和语音设备配置。候选仍必须完成签名验签、全量安装、逐文件 SHA-256 复验和“不启动游戏”的进程构建冒烟测试。上传 OSS、部署 API 清单和更新目录版本是三个独立生产动作，执行前必须得到明确确认。发布后使用一次性隔离账号验证低等级拒绝、目标等级放行、清单验签和新增对象真实下载，并精确清理账号、会话和审计记录。

## 6. API 发布

发布前：

1. 创建 PostgreSQL custom-format 备份、同名 SHA-256，并确认 `pg_restore --list` 可读。
2. 备份环境文件、systemd 单元、当前发布链接、Nginx 站点和清单目录。
3. 记录当前 `current` 指向和已知可用回滚版本。
4. 在远端临时路径校验上传归档 SHA-256。

部署时使用 [`install-release.sh`](../deploy/linux/install-release.sh) 原子切换。脚本只重启 `hechao-launcher-api.service`，不得顺带操作任何 Minecraft 服务。

部署后必须确认：

```text
本机 /healthz                     200
本机 /readyz                      200，database=ready
公网 /healthz 与 /readyz          200
匿名目录                          过渡期 200，强制登录后 401
无效 Bearer                       401
hechao.world                      200
api.hechao.world                  200
admin.hechao.world                未启用时 404
公网 8090                         不可连接
部署后 journal                    无新增 warning/error
```

涉及账号、权限、目录或分发的版本还要使用唯一隔离账号/记录完成真实事务回归，并在结束后精确清理测试用户、会话、身份、授权和审计数据。

对象下载限流必须与登录防刷分开。当前 `downloads` 策略按赫朝账号使用容量 `192`、每秒 `80` 的令牌桶；登录仍为每 IP 每分钟 `10`，全局仍为每 IP 每分钟 `6000`。调整后必须检查对象端点的 302/429 比例、`Retry-After`、旧官网与中转 API，不得通过关闭全局限流换取下载速度。

## 7. 启动器灰度

安装包只上传到私有 OSS 的版本固定路径，不覆盖同版本对象。管理员使用发布器 `0.9.0`
或更高版本先校验本地 SHA-256，再校验或写入远端对象，并生成最长 24 小时的内部下载
链接。发布 RAM 在安装包流程中只访问 `releases/launcher/*`；备份服务额外使用独立的
`backups/database/*` 与 `backups/recovery/*` 前缀，API 进程本身不持有这些前缀的
写权限。生产 systemd 单元分别读取 `/etc/hechao-launcher-api/environment` 和
`/etc/hechao-offsite-backup/environment`，禁止把发布 RAM AccessKey 写回 API 环境文件。

开放给内部成员前：

1. 确认无签名的永久直链返回拒绝访问。
2. 使用短时链接下载一次，核对文件大小与 SHA-256。
3. 只把短时链接发给本轮测试成员，不写入 Git、文档、公开群公告或网站。
4. 记录对象键、版本、大小、SHA-256、链接到期时间和测试范围，不记录完整签名链接。
5. 链接到期或泄露后重新生成；不得把 Bucket 或对象改成公共读。

灰度顺序：

1. 管理员本机验收。
2. 2 至 3 名内部成员验证安装、登录、更新、修复与启动。
3. 5 人灰度，覆盖普通成员和活动成员。
4. 20 人灰度，观察并发下载、登录失败、磁盘不足和不同活动档案。
5. 一个完整观察周期无阻断故障后再扩大。

每一档都要验证：

- 全新安装和旧版本覆盖升级。
- Microsoft 浏览器回调与赫朝会话恢复。
- 下载中断后续传、损坏文件修复和客户端版本回滚。
- 大厅、生存服、活动服目录与权限显示。
- 服务器维护、关闭、权限变更和 API 暂时不可用。
- 旧官网、中转 API、Velocity 与现有 Minecraft 服务没有受到影响。
- 运行 `tools/acceptance/Test-HechaoGrayPilotReadiness.ps1`，保存每档机器证据；任何
  活动 Critical、TPS/MSPT/GC 超阈值、API p95 超阈值或大厅出现玩家都立即停止扩大。
- 灰度工具必须在玩家进入前启动。schema `2` 证据会匿名统计本轮 fresh grant、
  等级覆盖、拒绝原因和目标人数；五人 monitor 阶段必须覆盖四级账号及
  `LaunchGrantRequired`、`InsufficientTier`、`AccessDenied`、
  `ServerUnavailable`。版本和档案不兼容继续由生产兼容矩阵覆盖，不重新开放已取消的
  游戏内转服路径。
- monitor 证据必须先通过 `Test-HechaoAuthorizerEnforceGate.ps1`，再由
  `Set-HechaoVelocityAuthorizerMode.ps1` 在零连接窗口切换。切换后重新生成
  enforce 证据并通过同一闸门，最后才可运行
  `Set-HechaoCatalogAuthentication.ps1`。两个切换工具默认只读，使用 `-Apply`
  时会先备份且失败自动恢复。完整命令和回滚边界见
  [`GRAY_PILOT_AUTHORIZATION_CUTOVER.md`](GRAY_PILOT_AUTHORIZATION_CUTOVER.md)。

`2026-07-30` 的严格 60 秒 Readiness 基线为 API `22/22` 成功、p95
`180.846 ms`、Activity `paused-when-empty`、大厅 `0` 人且最终活动 Critical
为 `0`。Survival1 已按真实停服状态改为 `Closed`，修订号从 `2` 增至 `3`，
数据库快照、事务审计和告警自动恢复均已验证；当前仅保留
`server:pvp:disk` Warning，不能把 Warning 当作多人容量已验收。

真实四级账号、Velocity `monitor` 灰度和 `enforce` 验收未完成前，不向全部玩家开放强制登录版本。

`0.11.16` 首轮 2 至 3 人测试按
[`PRELAUNCH_PILOT_0.11.16.md`](PRELAUNCH_PILOT_0.11.16.md) 执行。该手册的“可以开始”
只表示客户端小范围灰度前置条件已具备，不代表管理员 MFA、四级账号、Velocity
`enforce` 或生产世界备份已经验收。

## 8. 玩家公告内容

每份正式公告至少包含：

```text
启动器版本：
发布日期：
官方下载地址：
安装包大小：
SHA-256：
Windows 签名状态：
主要变化：
已知限制：
最低可用磁盘：
回滚/求助方式：
```

公告链接到 [`PLAYER_INSTALLATION_GUIDE.md`](PLAYER_INSTALLATION_GUIDE.md)，不得要求玩家关闭系统防护、提交密码、发送浏览器地址栏或使用第三方登录凭据。

## 9. 回滚

### 启动器

- 停止继续宣传问题版本，恢复上一不可变安装包的正式链接。
- 保留问题版本、哈希、日志和提交用于分析，不覆盖或删除。
- 启动器降级不得删除 `%LocalAppData%\Hechao\GameData` 或 `%LocalAppData%\Hechao\Launcher`。

### 客户端档案

- 将目录记录恢复到上一已签名清单和版本。
- 保留新对象；内容寻址对象不可覆盖，回滚不需要删除 OSS 文件。
- 启动器会保留一个 `.previous`，但生产回滚仍必须使用已记录的正式清单。

### API

- 将 `/opt/hechao-launcher-api/current` 原子切回已知可用发布并只重启 API。
- 加法迁移可以保留；未经独立恢复方案不得删除表、字段、审计或账号数据。
- 回滚后重新执行健康、认证、公网端口、旧官网和中转 API 回归。

## 10. Git 与发布收口

1. 确认源码、测试和文档进入范围清楚的提交。
2. `git diff --check` 与暂存区秘密检查通过。
3. 推送 `main`，核对本地与 `origin/main` 提交一致。
4. 为正式组件创建注释标签并推送，例如 `api-v0.10.1`。
5. 把发布 ID、程序/归档哈希、备份、回归、当前开关和回滚目标写入对应运维文档。
6. 确认工作区干净，再结束发布。

只完成构建而没有部署，写“候选”；只部署代码但开关关闭，写“代码已部署、功能未激活”；只有完成灰度和对外公告后，才能写“已向玩家发布”。

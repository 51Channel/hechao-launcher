# 整合包自动导入与受控目标部署手册

> 当前生产：API `0.30.2`、Publisher Agent `1.2.1`、owl5
> ServerControlAgent `0.5.0`、owl9 ServerControlAgent `0.4.0`。
>
> 当前状态：固定试包已完成上传、识别、客户端私有 OSS `Test` 发布和停止活动槽部署；
> Publisher 已迁移到 API 同机阿里云 systemd，Windows 计划任务保持停止回滚状态。
> Gray/Production 未变化。测试服务端随后归档，原活动服从受控回滚目录恢复并保持停止。
> 当前生产链保留目标级目录访问门闩、Windows 瞬时目录占用重试、部署身份、受控服务端
> 目录删除和 VPS 物理内存上报。
>
> 源码候选 API `0.31.1` / ServerControlAgent `0.6.0` 将部署范围扩展为代理配置中显式
> 设置 `packageDeploymentEnabled=true` 的合法目标。候选尚未部署生产；活动企划仍只使用
> 固定 `activity` 槽，不受本次通用导入能力影响。

本功能允许管理员在后台上传一个 ZIP 或 MRPACK 整合包，先自动识别并拆分客户端与
服务端，再经人工确认完成客户端私有 OSS 发布，并选择“仅发布并入库”或“立即部署受控
目标”。活动企划默认只入库服务端制品，随后在活动企划日历中绑定和部署。自动识别
只减少整理文件的工作，不替代组件计划、许可证核对、玩法测试或管理员审批。

## 1. 固定架构边界

- 玩家仍通过赫朝启动器选择服务器，并统一连接 Velocity 公网入口；导入功能不会开放
  后端公网端口，也不会删除或绕过 Velocity Authorizer。
- API 只接受服控 Agent 心跳中显式上报 `packageDeploymentEnabled=true`，且 server ID、
  Agent ID 和端口合法的目标。生产候选配置只为 `activity` 与 `survival2` 开放；其他目标
  继续故障关闭。活动企划自己的部署端点仍严格固定到 `activity / owl5 / 25568 /
  owl5-activity-slot`。
- 后台存在多个受控目标时不预选；管理员必须明确选择目标。立即部署的精确确认文本包含
  目标 ID，例如 `发布并部署 <importId> 到 survival2`，避免长期包误投到活动槽。
- “仅发布并入库”不要求目标代理在线，也不会读取、停止或覆盖所选目标；只有选择
  “立即部署受控目标”或稍后从企划页部署时，目标才必须停止且不能有其他活动操作。流程
  不会为了导入自动停止冲突服，也不会在部署成功后自动启动 Minecraft。
- 已删除并完成清理的受控目录不会出现在普通服控列表，但整合包页会显式读取保留的目标
  配置；代理在线、目标停止且部署能力有效时可以直接重新部署新服务端。
- owl5 代理上报 VPS 真实物理内存。后台显示 VPS 总内存和推荐最小、最大内存；推荐
  区间只用于提示，不会禁用确认按钮或阻止管理员提交。推荐最小值按主机内存的八分之一
  计算并限制在 `4-8 GiB`，推荐最大值按一半计算并限制在 `1-16 GiB`，均向下对齐
  `256 MiB`。
- 受控目标部署不使用 `4096 MiB` 回退上限或旧的 `8192 MiB` 人工配置限制。代理只以
  VPS 物理容量和 `64 GiB` 技术边界防止无效 JVM 参数；目录存在与否不改变建议规则。
- 客户端签名发布只进入 `Test` 通道，不覆盖或推进 `Gray`、`Production`，也不覆盖
  已存在的 OSS 对象、签名清单或 Git 标签。
- 可选的目录同步只创建或更新与目标 ID 同名、Velocity 目标同名、隐藏且 `Closed` 的
  目录记录。玩家可见性和正式开放仍需
  管理员在独立发布步骤中处理。
- 成功部署后保留一个受控回滚目录，服务端保持停止。失败时优先恢复原目录；自动恢复
  不完整会返回独立错误码，禁止继续启动目标。

## 2. 接受的归档

后台只接受文件名安全、大小在配置上限内的 `.zip` 或 `.mrpack`。API 在受限暂存目录
中按 8 MiB 分块接收，支持暂停、断点续传和取消。完成后重新计算完整 SHA-256，再由
安全归档分析器检查：

- 路径穿越、绝对路径、重复路径、符号链接、重解析点和危险设备名；
- 文件数量、单文件大小、总解压大小和压缩比；
- Minecraft 版本、加载器、客户端与服务端文件归属；
- 可疑可执行文件、秘密文件名和无法安全归类的内容。

必须同时识别出客户端与服务端，且没有 `Blocking` 问题，后台才允许确认。管理员仍要
逐项核对识别出的版本、加载器、文件样本和全部警告。

服务端部分必须包含：

- `server.properties`；部署时强制写入 `server-ip=127.0.0.1`、配置目标自己的端口
  和 `online-mode=false`；
- 目标配置指定的 JVM 参数文件，且能由代理写入唯一的 `-Xms` 与 `-Xmx`；
- 与 owl5 受管计划任务一致的 `start.bat`；脚本必须单独包含：

```bat
if not defined HECHAO_MANAGED_START pause
```

该标记使计划任务和手工双击使用同一个明确入口。缺少脚本、扩展名不为 `.bat`、标记
不匹配或计划任务引用了另一个启动脚本时，安装和部署均失败关闭。

## 3. 发布与部署流程

1. 管理员在“整合包导入”页选择 ZIP/MRPACK。前端创建上传任务并以 8 MiB 分块上传；
   刷新或暂停后可重新选择同名同大小文件续传。
2. API 完整校验上传并异步分析。存在阻断项时只能取消并修复源包，不能强制跳过。
3. 管理员填写档案 ID、显示名、语义版本、最低称号、最大内存、世界保留策略和目录
   同步策略，明确选择受控目标，再选择“仅发布并入库”或“立即部署受控目标”，并输入
   任务和目标专属确认文本。页面
   同时显示 VPS 总内存和推荐区间；超出推荐区间会提示但仍可提交。
4. 独立 Publisher Agent 领取带租约的任务，下载客户端归档，使用现有生产
   P-256 私钥生成签名清单，并以内容 SHA-256 上传缺失 OSS 对象。已存在对象必须同时
   匹配长度和摘要元数据，否则拒绝覆盖。
5. API 验证签名清单、公钥信任、档案元数据和对象闭合关系，创建不可变发布记录，并
   只设置 `Test` 通道。
6. 选择“仅发布并入库”时，API 直接进入收口：设置 `Test` 通道、保留服务端制品并把
   任务标记为 `Completed`，不会创建服控命令。选择“立即部署受控目标”时，API 才为同一
   导入创建结构化 `DeployPackage` 命令。
7. 立即部署时，只有目标所属且持有有效命令租约的服控代理可以使用 Range 下载服务端归档；代理
   再次校验摘要、大小、清单、目标、端口、停止状态和目录所有权，并在同卷暂存目录解压。
8. 代理原子切换目标目录并保留一个回滚目录。API 读回成功结果后完成 `Test` 发布和
   可选隐藏目录同步，任务进入 `Completed`，目标仍保持停止。只入库任务可以稍后由
   活动企划页对绑定整合包执行同样的结构化部署。

完成客户端与服务端测试后，管理员还需要从客户端档案后台把精确清单推进到
`Production`，活动企划才允许发布给玩家。企划操作见
[`ACTIVITY_PLAN_OPERATIONS.md`](ACTIVITY_PLAN_OPERATIONS.md)。

Publisher 下载客户端归档时最多执行三次可续传重试；API 重启或网络中断后，只有真正
执行中的任务通过心跳长期续租。未执行任务的租约会过期并重新领取，不会永久卡死。

客户端发布期间，Publisher 最多每两秒向受租约保护的内部端点上报一次当前阶段、已处理
对象数和字节数。后台沿用 3 秒任务轮询显示真实进度条；下载归档和 OSS 对象阶段显示
百分比，解压与构建阶段显示不确定进度。预计剩余时间只在同一阶段取得两个递增样本后
计算，样本不足时明确显示“正在计算剩余时间”。OSS 中已存在且通过长度、摘要复核的
对象同样计入已处理进度，失败或取消任务保留最后一次进度用于排查。Publisher 因磁盘
门禁等待工作空间时只显示当前可用量和所需量，不把磁盘空闲量伪装成发布百分比或 ETA。

## 4. API 配置

首次启用时使用 Publisher 令牌脚本生成 DPAPI `CurrentUser` 密文。脚本必须在运行
Windows 回滚代理的同一账号下，以提升权限的 PowerShell 7 执行；它只输出令牌文件
路径和 SHA-256，不输出明文令牌：

```powershell
pwsh -NoLogo -NoProfile -File `
  .\deploy\windows\package-publisher\New-PackagePublisherAgentToken.ps1
```

把输出的 SHA-256 通过受保护运维通道提供给 API 主机。不要把摘要、DPAPI 文件、私钥
或 OSS 凭据写入 Git、文档、聊天或构建日志。API 主机使用无秘密脚本备份环境文件、
创建 `0700` 暂存目录并写入上限；脚本不会重启 API：

```bash
sudo bash deploy/linux/configure-package-imports.sh \
  /etc/hechao-launcher-api/environment true <publisher-token-sha256>
```

systemd 单元必须允许 API 同时写入
`/var/lib/hechao-launcher-api/package-imports` 和
`/var/lib/hechao-launcher-api/manifests`。前者保存上传与分析状态，后者在 Publisher
完成客户端发布回调后落盘签名发布清单；任一路径在 `ProtectSystem=strict` 下遗漏都会让
任务失败。正式启用前先部署更新后的单元并执行 `systemctl daemon-reload`；只有制品、
数据库备份、Publisher 和 owl5 代理均验收后，才重启 API。回滚开关使用同一脚本写入
`false`，不删除暂存目录：

`manifests` 根目录继续保持 `root:hechao-api 0750`，既有根级正式清单不放宽权限；
`configure-distribution.sh` 只把 `manifests/releases` 预建为
`hechao-api:hechao-api 0750`，供 API 创建按档案和摘要寻址的不可变发布清单。

```bash
sudo bash deploy/linux/configure-package-imports.sh \
  /etc/hechao-launcher-api/environment false
```

## 5. Publisher Agent 安装与迁移

### 5.1 阿里云 Linux 主实例

Publisher `1.1.0` 支持 Linux `systemd-credentials`。生产主实例使用独立
`hechao-publisher` 无登录账户，程序、配置、状态和凭据分别位于：

- `/opt/hechao-package-publisher`；
- `/etc/hechao-package-publisher/agent.json`；
- `/var/lib/hechao-package-publisher`；
- `/etc/credstore.encrypted/hechao-package-publisher`。

配置使用
[`package-publisher-agent.example.json`](../deploy/linux/package-publisher/package-publisher-agent.example.json)
作为模板。`tokenPath`、`signingKeyPath` 和 `ossCredentialPath` 只能填写三个固定的
systemd 凭据名，不能填写绝对路径或明文。`minimumFreeBytes` 是任务完成后仍需保留的
磁盘余量；实际领取后还会按
`压缩包 + 2 * 压缩包 * workingSpaceExpansionMultiplier` 预留工作空间。空间不足时
代理继续心跳并持有租约等待，不下载、不解压，也不把任务误报为失败。

先构建自包含单文件 `linux-x64` 制品，并记录大小和 SHA-256。现有 DPAPI 令牌、签名
私钥和 OSS 凭据只能通过下面的 PowerShell 7 脚本迁移；脚本在内存中解密后直接写入
SSH 标准输入，由远端 `systemd-creds --with-key=host` 加密，不创建本地或远端明文
中间文件：

```powershell
pwsh -NoLogo -NoProfile -File `
  .\deploy\windows\package-publisher\Install-PackagePublisherSystemdCredentials.ps1 `
  -WindowsConfiguration <现有 Windows agent.json> `
  -Remote root@<阿里云主机> `
  -IdentityFile <运维私钥> `
  -KnownHostsFile <固定主机密钥文件>
```

迁移签名私钥到 API 所在主机会扩大单机失陷影响面。必须保留独立加密恢复材料，限制
SSH root 登录，使用最小权限 Publisher RAM 凭据，并保持 API 进程无权读取 Publisher
的加密凭据目录。加密凭据与当前阿里云系统主机密钥绑定，不能把三个 `.cred` 文件单独
复制到另一台机器当作恢复方案。

使用实际制品、配置、SHA-256、发布 ID 和仓库中的 systemd 单元安装。默认只暂存且不
启用服务；只有确认发布队列为空、本机 Windows 代理已停止后，才加 `--start`：

```bash
sudo bash deploy/linux/package-publisher/install-package-publisher.sh \
  <Hechao.Publisher> <agent.json> <sha256> <release-id> \
  deploy/linux/package-publisher/hechao-package-publisher.service

sudo bash deploy/linux/package-publisher/install-package-publisher.sh \
  <Hechao.Publisher> <agent.json> <sha256> <release-id> \
  deploy/linux/package-publisher/hechao-package-publisher.service --start
```

切换成功必须同时满足：Linux 服务 `active`、版本心跳为 `1.1.0`、队列没有
`QueuedForPublishing`/`PublishingClient`、API 健康与就绪仍为 `200`、journal 无
秘密或持续错误，并通过一次系统服务重启恢复。失败时先停止并禁用 Linux 服务，再恢复
本机计划任务；同一时刻禁止两端共同领取任务。

### 5.2 Windows 回滚实例

复制
[`package-publisher-agent.example.json`](../deploy/windows/package-publisher/package-publisher-agent.example.json)
到仓库外受限目录，填写 API 地址、代理 ID、DPAPI 令牌、现有签名私钥、独立 OSS 发布
凭据和状态目录。配置文件只保存路径和公开元数据，不保存明文秘密。

构建自包含 `win-x64` Publisher 后，以实际 SHA-256 安装：

```powershell
pwsh -NoLogo -NoProfile -File `
  .\deploy\windows\package-publisher\Install-PackagePublisherAgent.ps1 `
  -PublisherExecutable <Hechao.Publisher.exe> `
  -Configuration <package-publisher-agent.json> `
  -ExpectedSha256 <publisher-exe-sha256>
```

安装器核对 EXE、配置和三个受保护输入，备份旧 EXE、配置与计划任务，再原子替换并收紧
ACL。任一步失败会恢复旧文件和旧任务。迁移完成后 Windows 计划任务保持停止，但不删除
EXE、配置或 DPAPI 文件；Linux 主实例故障且没有活动租约时，先停用 Linux 服务，再由
原账号手工恢复 Windows 计划任务。计划任务使用 DPAPI 同一用户登录会话，更换运行账号
会导致启动失败。

## 6. 服控代理目标配置

每个允许整合包原子部署的目标都必须单独显式设置。当前 owl5 候选只开放 `activity` 和
`survival2`：

```json
{
  "serverId": "survival2",
  "port": 25565,
  "conflictGroup": "owl5-survival-slot",
  "startScriptRelativePath": "start.bat",
  "packageDeploymentEnabled": true,
  "hostManagedRelativePaths": ["forwarding.secret"],
  "worldDataRelativePaths": ["world", "world_nether", "world_the_end"]
}
```

`hostManagedRelativePaths` 必须已存在于旧受控目录，并在复制前通过重解析点检查；上传包
不能提供或替换这些文件。`worldDataRelativePaths` 只有管理员勾选“保留世界”时才迁移。
两组路径彼此不能重复、嵌套或重叠，也不能覆盖 `server.properties`、JVM 参数文件、
启动脚本或部署所有权标记。代理会在读取配置时失败关闭，而不是等到目录切换后处理。

更新代理前，`Install-ServerControlAgent.ps1` 会确认每个已存在目标目录里的受管启动
脚本带标记，并确认对应 `Hechao-Server-<target>` 计划任务明确引用同一绝对路径。该检查只验证
下一次启动契约，不会启动、停止或重启 Minecraft。

生产 owl5 当前正式代理为 `0.5.0`；通用目标候选为 `0.6.0`，尚未部署。目录心跳、快捷设置和目录切换使用同一目标级访问门闩；
外部进程持续持有目录句柄时仍会失败并恢复旧目录，不会绕过 Windows 文件锁或终止未知
进程。管理员必须先确认占用者确实是无 Java 子进程的遗留包装进程，才能单独处理它。

## 7. 发布顺序与回滚

首次生产接入顺序：

1. 备份 PostgreSQL、API 环境文件、当前 API 制品、owl5 代理配置/EXE/任务，以及活动
   服务端目录和世界；逐项验证恢复路径。
2. 部署 API `0.27.0` 并连续应用迁移 `022-023`，但保持
   `PackageImports__Enabled=false`。
3. 更新 systemd 单元、Publisher Agent 和 owl5 ServerControlAgent `0.3.0`；只核对
   心跳、版本、目标能力和 PID，不操作任何 Minecraft 服务端。
4. 启用 PackageImports 后只重启 API，验证健康、就绪、迁移数、后台第十个路由和两个
   代理心跳。
5. 使用不含秘密、无玩家、可丢弃世界的专用测试包完成上传、识别、Test 发布、停服
   部署和目录隐藏验证；确认 Velocity 路由与其他 Java PID 均未变化。

API 或后台异常时，先把 `PackageImports__Enabled=false`。在尚未产生任何
`DeployPackage` 操作记录前，可以恢复上一 API 制品，迁移 `022-023` 的空表、新增列和
动作约束可保留。一旦已有部署操作记录，旧 API `0.26.2` 无法识别新动作枚举，禁止直接
替换旧二进制；应保持 `0.27.x`、关闭导入并向前修复，灾难恢复时才使用同一时点的完整
数据库与 API 备份。存在待处理部署命令时也不能把 owl5 代理降回 `0.2.4`。

Publisher 异常时停止其计划任务并使用安装器备份恢复。服务端切换失败且自动回滚成功时
继续保持停止；若自动回滚不完整，禁止启动并人工核对 `.hechao-rollback` 所有权标记后
恢复。任何回滚都不删除审计、导入记录或已经产生的不可变 OSS 对象。

成功部署后的旧服务端位于受控回滚目录，目前没有网页“一键回滚服务端”动作。需要
回滚时必须先保持目录 `Closed/Maintenance`、确认零玩家和目标停止，备份当前目录，再由
管理员按服控手册人工恢复旧目录；恢复后仍不自动启动。

## 8. 完成门槛

- ZIP/MRPACK、暂停续传、错误偏移、取消、阻断分析和损坏归档均有自动测试；
- 客户端签名、OSS 已存在对象校验、API 中断恢复和 Test-only 通道均通过；
- 服务端固定目标、回环监听、`online-mode=false`、启动脚本、固定文件、世界保留、
  重解析点、原子切换和失败回滚均通过；
- 桌面和 390px 移动后台无横向溢出、遮挡或不可达操作，浏览器控制台无错误；
- 完整解决方案、前端单元与 Playwright 全部通过，Git 差异无秘密和构建产物；
- 生产固定试包、真实 OSS、真实 owl5 停服部署和原活动目录人工恢复均已有独立证据；
  真人进服与真实玩法整合包仍属于后续活动验收，不能由固定空包替代。

## 9. 生产验收（2026-08-05）

生产组件与构建来源：

- API `0.27.0-20260803T174833Z`、Publisher Agent `1.0.0` 来自提交
  `f0616a69e95a6dd6ff172369a4bb8883e4e6ab0b`；
- owl5 ServerControlAgent `0.3.1` 来自提交
  `784c05d8ba172a594a8d95c47c14db253e1cb53a`；
- owl9 保持 `0.2.1`，Velocity、游戏服启动任务和客户端正式通道均未修改。

失败路径先于成功路径得到验证：清单落盘权限缺口和 `0.3.0` 心跳读取竞争均没有留下
半成品；`0.3.1` 遇到一个由旧计划任务遗留、没有 Java 子进程的 `cmd.exe` 持续占用
活动目录时，也按设计失败并恢复原目录。使用 Microsoft 签名的 Handle 工具确认唯一
占用者后，只终止该孤立包装进程；五个 Minecraft Java PID 未变化。

最终固定试包结果：

- Import ID：`b4620e53-f125-4749-b220-101d17189cc4`；
- 客户端版本：`0.0.3-e2e.20260804.022606`；
- Manifest SHA-256：
  `9a6938025ad7e2c620d87e83579e669c27ca8676d79e07798694c2b542af7f50`；
- 服务端操作 ID：`34eb401b-dd9e-4dcb-a495-88a3022bc258`；
- Publisher 复核并跳过 `4` 个已存在对象，上传新对象 `0` 个；
- `Test` 通道为 `100%`，Gray 和 Production 继续没有该测试发布；
- 同步目录 `activity` 为隐藏、`Closed`，活动目标上报离线。

部署完成后，在活动任务 `Ready`、`25568` 无监听、API 无进行中命令或导入任务的条件下，
原活动服从 `E:\.ActivityNeoForge.hechao-rollback` 原子恢复。恢复前后的原服受控树摘要
一致；最终 `E:\ActivityNeoForge` 为 `326` 个文件、`212,626,569` 字节，包含世界、
启动脚本、服务端配置和主机固定转发文件，不包含测试部署标记。无秘密测试目录以 `8`
个文件归档到 `E:\manual-backups\package-import-e2e`，归档不含主机固定文件；回滚目录、
owner 和临时目录均已清理。

最终五个 Java PID 仍为 `2576 / 6008 / 7748 / 9428 / 10412`，活动任务为 `Ready`，
`25568` 无监听，服控代理为单实例 `Running`。API 内外网健康与就绪均正常、数据库
`ready`、迁移 `23/23`、`NRestarts=0`，恢复窗口以来错误级日志为 `0`。

结构化证据见
[`evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json`](evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json)
和
[`evidence/SERVER_CONTROL_AGENT_0.3.1_PRODUCTION_DEPLOYMENT_2026-08-05.json`](evidence/SERVER_CONTROL_AGENT_0.3.1_PRODUCTION_DEPLOYMENT_2026-08-05.json)。

# 整合包自动导入与活动槽部署手册

> 当前生产：API `0.27.0`、Publisher Agent `1.0.0`、owl5 ServerControlAgent
> `0.3.1`；owl9 ServerControlAgent 保持 `0.2.1`。
>
> 当前状态：固定试包已完成上传、识别、客户端私有 OSS `Test` 发布和停止活动槽部署；
> Gray/Production 未变化。测试服务端随后归档，原活动服从受控回滚目录恢复并保持停止。
> `0.3.1` 使用目标级目录访问门闩隔离独立心跳与切换阶段，并对 Windows 瞬时目录占用
> 做短时有界重试。完整解决方案 `633/633`、API `268/268`、Publisher `39/39`、
> ServerControlAgent `46/46`、Vitest `8/8` 和 Playwright `14/14` 已通过。

本功能允许管理员在后台上传一个 ZIP 或 MRPACK 整合包，先自动识别并拆分客户端与
服务端，再经人工确认完成客户端私有 OSS 发布和 owl5 活动槽服务端部署。自动识别只
减少整理文件的工作，不替代组件计划、许可证核对、玩法测试或管理员审批。

## 1. 固定架构边界

- 玩家仍通过赫朝启动器选择服务器，并统一连接 Velocity 公网入口；导入功能不会开放
  后端公网端口，也不会删除或绕过 Velocity Authorizer。
- 服务端只允许部署到 `activity / owl5 / 127.0.0.1:25568 / owl5-activity-slot`。
  `survival2`、`lobby`、`pvp`、`fanstreet`、`yugong` 和其他目标均被 API 与代理双重拒绝。
- 确认发布前，owl5 代理必须在线、目标必须停止且不能有其他活动操作。流程不会为了
  导入自动停止冲突服，也不会在部署成功后自动启动 Minecraft。
- 客户端签名发布只进入 `Test` 通道，不覆盖或推进 `Gray`、`Production`，也不覆盖
  已存在的 OSS 对象、签名清单或 Git 标签。
- 可选的目录同步只创建或更新隐藏且 `Closed` 的活动记录。玩家可见性和正式开放仍需
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

- `server.properties`；部署时强制写入 `server-ip=127.0.0.1`、`server-port=25568`
  和 `online-mode=false`；
- `user_jvm_args.txt`，且能由代理写入唯一的 `-Xms` 与 `-Xmx`；
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
   同步策略，并输入任务专属确认文本。
4. 独立 Windows Publisher Agent 领取带租约的任务，下载客户端归档，使用现有生产
   P-256 私钥生成签名清单，并以内容 SHA-256 上传缺失 OSS 对象。已存在对象必须同时
   匹配长度和摘要元数据，否则拒绝覆盖。
5. API 验证签名清单、公钥信任、档案元数据和对象闭合关系，创建不可变发布记录，并
   只设置 `Test` 通道。
6. API 为同一导入创建结构化 `DeployPackage` 命令。只有持有有效命令租约的 owl5
   代理可以使用 Range 下载对应服务端归档。
7. 代理再次校验归档摘要、大小、清单、目标、端口、服务端停止状态和目录所有权，在
   同卷暂存目录解压；主机固定文件只能从旧受控目录保留，不能由上传包覆盖。
8. 代理原子切换活动目录并保留一个回滚目录。API 读回成功结果后完成 `Test` 发布和
   可选隐藏目录同步，任务进入 `Completed`，目标仍保持停止。

Publisher 下载客户端归档时最多执行三次可续传重试；API 重启或网络中断后，只有真正
执行中的任务通过心跳长期续租。未执行任务的租约会过期并重新领取，不会永久卡死。

## 4. API 配置

先使用 Publisher 令牌脚本生成 DPAPI `CurrentUser` 密文。脚本必须在将要运行计划任务
的同一 Windows 账号下，以提升权限的 PowerShell 7 执行；它只输出令牌文件路径和
SHA-256，不输出明文令牌：

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

## 5. Publisher Agent 安装

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
ACL。任一步失败会恢复旧文件和旧任务。默认不启动代理；只有完成只读配置复核后才显式
加 `-StartAgent`，或由管理员手工启动计划任务。计划任务使用 DPAPI 同一用户登录会话，
更换运行账号会导致启动失败。

## 6. owl5 代理配置

只有 `activity` 目标可设置：

```json
{
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

更新代理前，`Install-ServerControlAgent.ps1` 会确认活动目录里的 `start.bat` 带受管
标记，并确认 `Hechao-Server-Activity` 计划任务明确引用同一绝对路径。该检查只验证
下一次启动契约，不会启动、停止或重启 Minecraft。

owl5 当前正式代理为 `0.3.1`。目录心跳、快捷设置和目录切换使用同一目标级访问门闩；
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

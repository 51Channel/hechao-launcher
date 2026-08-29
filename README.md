# 赫朝启动器

> `2026-08-29` 天域远征 Screen `0.2.13` 将每名玩家的 RTP 成功冷却从 `60` 秒调整为
> `10` 秒。安全落点仍在异步查找，查找完成前的重复请求继续拒绝；范围、边界、安全策略、
> 超时和网络协议均未改变，现有客户端无需更新。详情见
> [`docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.13.md`](docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.13.md)。

> `2026-08-29` 天域远征 Screen `0.2.12` 已完成服务端热修。`0.2.11` 虽把候选区块改为
> Future 加载，但落点的 `level.noCollision` 仍会在主线程同步索取相邻区块，已于 8 月 28、
> 29 日再次触发 60 秒 Watchdog。新版完全移除世界碰撞扫描，只读取 Future 返回的
> `LevelChunk`，并要求玩家碰撞箱完整位于已经验证为空气的两格柱体内。最大范围仍为
> `5000` 格、冷却仍为 `60` 秒，协议保持 `3`，无需重新下载客户端。详情见
> [`docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.12.md`](docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.12.md)。

> `2026-08-24` HechaoEconomy `0.2.4` 已部署到 `activity-survival`，并与 API `0.37.0`
> 完成官方商城购买和待领取闭环。服务端配置明确使用 `server-id=activity-survival`；其他
> 独立槽仍必须配置自己的身份。详情见
> [`docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.2.4.md`](docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.2.4.md)。

> `2026-08-23` Tom's Simple Storage `2.4.1` 已成对部署到天域远征工业季服务端和客户端
> 档案 `1.0.29`，后台仅切换到 `Test=100% / r22`。1.0.29 在 1.0.28 基线上只新增
> `toms_storage-1.21-2.4.1.jar`，原有 `4,457` 个文件逐哈希不变；签名清单、OSS 两轮对象
> 闭合、生产 API、owl5 Arclight 冷启动、单端口和 Tom's Storage 双配置加载均已验收。
> Gray 与 Production 仍未分配，真人增量下载、库存连接、存取搜索、多人并发与重启持久化
> 验收完成前不得推进。详情见
> [`docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.29.md`](docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.29.md)。

> `2026-08-22` 天域远征 Screen `0.2.9` 已与客户端档案 `1.0.28` 成对发布到
> `Test=100% / r21`。快捷首页现有 15 项操作，新增 `/rtp` 随机传送；RTP 按当前维度
> 世界边界计算范围，最大 `5000` 格、边界保留 `32` 格并有 `60` 秒玩家冷却。返回主城
> 继续由 EssentialsXSpawn 执行，服主可在目标位置使用 `/setcity` 更新主城，权限等级为
> `2`。服务端已完成零玩家保存、完整离线备份和 Arclight 冷部署；`109/109` 测试、
> 可复现构建、签名清单、OSS 对象闭合和生产 API 验收均通过。Gray 与 Production 仍未
> 分配，真人 RTP、主城设置与 15 项菜单验收完成前不得推进。详情见
> [`docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.9.md`](docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.9.md)
> 和 [`docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.28.md`](docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.28.md)。

> `2026-08-21` 官方回收部分额度修复已联动发布：API `0.36.1` 会把请求数量裁剪到
> 个人与全服剩余额度，HechaoEconomy `0.2.3` 只托管报价数量并把余量保留在出售槽。
> `64` 个苹果遇到个人上限 `32` 时将报价 `32`、保留 `32`，不再整组拒绝。完整 .NET、
> 隔离 PostgreSQL、Bukkit 测试、可复现构建和生产 `64 -> 32` 报价烟测均已通过；客户端
> Screen `0.2.7` 和档案 `1.0.26` 未变。详情见
> [`docs/API_RELEASE_0.36.1.md`](docs/API_RELEASE_0.36.1.md) 和
> [`docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.2.3.md`](docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.2.3.md)。

> `2026-08-20` 玩家市场排序与单位价切片已形成本地候选：API `0.36.0`、HechaoEconomy
> `0.2.2`、Screen `0.2.7`。四种读取排序、稳定 SQL 顺序、单位价字段和游戏内排序按钮已
> 完成自动测试；未上传、未部署、未切换任何生产通道。详情见
> [`docs/SKYREALM_PLAYER_MARKET_0.2.2_CANDIDATE.md`](docs/SKYREALM_PLAYER_MARKET_0.2.2_CANDIDATE.md)。

> `2026-08-19` 天域远征第三方屏幕 `0.2.3` 已随客户端档案 `1.0.23` 发布到
> `Test=100% / r16`。上架物品页面收回规则区域，避免与玩家背包槽重叠，并拦截隐藏服务端
> 控制槽的误点击；我的队伍页面增加成员列表、点击成员自动填入操作框、队长移出保护和
> 更宽容的队伍状态解析。客户端仍只发送既有 `skyrealmcore:team` 命令，未新增服务端能力。
> Gray 与 Production 保持未分配，`1.0.22` 保留为回滚目标。记录见
> [`docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.3.md`](docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.3.md)。
> 档案记录见
> [`docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.23.md`](docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.23.md)。
>
> `2026-08-18` 天域远征玩家市场与全局模糊搜索已完成分阶段部署。API `0.35.0` 和
> HechaoEconomy `0.2.0` 已生产上线，客户端 Screen `0.2.0` 随不可变档案 `1.0.20`
> 只进入 `Test=r13 / 100%`；Gray 与 Production 未分配。市场支持上架、购买、下架、
> 待领取和中文/物品 ID/卖家模糊搜索，事务、幂等和余额由服务端裁决。隔离 PostgreSQL
> 验证及生产健康通过；双真人账号完整交易、断线、背包竞争、幂等重试和余额守恒仍待
> Test 验收。记录见
> [`docs/API_RELEASE_0.35.0.md`](docs/API_RELEASE_0.35.0.md)、
> [`docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.2.0.md`](docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.2.0.md)、
> [`docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.0.md`](docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.2.0.md)
> 与 [`docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.20.md`](docs/SKYREALM_INDUSTRIAL_PROFILE_RELEASE_1.0.20.md)。
>
> `2026-08-18` 工业季第三方屏幕已完成 Image2 工业远征风格改版。Screen `0.1.10`
> 使用生成的机械背景和齿轮罗盘徽记，统一导航、余额、出售结果和 85 项回收目录；业务、
> 权限、价格和网络协议均未改变。新版随档案 `1.0.19` 只发布到
> `Test=r12 / 100%`，Gray 与 Production 未分配；玩家退出旧游戏并由启动器增量更新后
> 仍需完成真人视觉与交互验收。记录见
> [`docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.1.10.md`](docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.1.10.md)。
>
> `2026-08-18` 工业季回收目录超时已修复。根因是生产 Arclight 缺少 Paper API 的
> `Material.translationKey()`，HechaoEconomy `0.1.6` 在渲染首个商品时抛出
> `NoSuchMethodError`，客户端因此等到超时。`0.1.7` 改为保留物品原生可翻译名称，
> 已通过受管停服、备份和冷启动上线；`22/22` 测试、可复现 JAR、插件健康与错误日志
> 均通过。真人重新进服点击仍待最终目视确认。记录见
> [`docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.1.7.md`](docs/SKYREALM_ECONOMY_PLUGIN_RELEASE_0.1.7.md)。
>
> `2026-08-18` 工业季完整官方回收目录 v2 已按明确要求启用：生产商品表从 `0` 项更新为
> `85` 项启用，逐字段回读 `85/85`，二次预览 `0` 差异。服务端 HechaoEconomy `0.1.6`
> 已完成受管冷更新，目录按 `45 + 40` 分两批；客户端 Screen `0.1.9` 随档案 `1.0.18`
> 发布到 Test，可浏览全部 85 项。当前单项额度有效，但跨商品金额总门禁、北京时间额度日
> 和部分数量回收仍待实现。记录见
> [`docs/SKYREALM_ECONOMY_CATALOG_V2_RELEASE.md`](docs/SKYREALM_ECONOMY_CATALOG_V2_RELEASE.md)。
>
> `2026-08-18` 工业季第三方屏幕已把余额、出售和回收目录改为直接打开模组原生业务 UI。
> Screen `0.1.7` 支持加载、成功、失败、超时、出售报价确认、空目录和响应式分页；价格、
> 权限、会话和交易仍由服务端裁决，协议保持 `2`。新版随档案 `1.0.16` 只进入
> `Test=r9 / 100%`，Gray 与 Production 未分配；与 `1.0.15` 共同的 `4,456` 个文件
> 完全不变，OSS 仅新增 `51,271` 字节对象。玩家退出旧游戏并由启动器增量更新后仍需完成
> 真人点击验收。记录见
> [`docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.1.7.md`](docs/SKYREALM_ECONOMY_SCREEN_RELEASE_0.1.7.md)

> `2026-08-18` 管理后台经济监控已随 API `0.34.0` 正式上线：新增跨服货币供给、财富
> 分布、玩家余额、官方回收流量，以及按真实成交聚合的单品小时/每日 K 线。迁移
> `032/033` 只增加分析索引，生产商品目录保持 `85/85`；当前生产尚无经济账户或成交，
> 页面按真实状态显示空行情，不生成演示蜡烛。发布记录见
> [`docs/API_RELEASE_0.34.0.md`](docs/API_RELEASE_0.34.0.md)。
>
> `2026-08-15` Launcher API `0.32.2` 已修复未发布客户端档案拖垮整份玩家目录的
> 问题。API 现在只下发当前玩家能够同时取得客户端档案的服务器；未发布服务器继续
> 保留在后台等待发布，不再阻断已有正式档案的赫朝商务追杀。生产实时目录已恢复为
> `activity / 赫朝商务追杀 / Online`，记录见
> [`docs/API_RELEASE_0.32.2.md`](docs/API_RELEASE_0.32.2.md)。
>
> `2026-08-15` 全权限 Minecraft 控制台已正式上线：Launcher API `0.32.1` 与
> owl5/owl9 ServerControlAgent `0.7.2` 统一使用
> `allowedCommandPrefixes=["*"]`，允许全部 Minecraft、模组和插件命令，包括
> `op/deop`、LuckPerms 与命名空间命令。`stop/restart/shutdown/end` 仍必须使用
> 结构化服控按钮。10 个目标心跳、双机配置、Agent 日志、公网后台和游戏进程隔离均已
> 验收；发布没有启动、停止、重启游戏服或发送控制台命令。记录见
> [`docs/API_RELEASE_0.32.1.md`](docs/API_RELEASE_0.32.1.md) 与
> [`docs/SERVER_CONTROL_AGENT_RELEASE_0.7.2.md`](docs/SERVER_CONTROL_AGENT_RELEASE_0.7.2.md)。

> `2026-08-15` 独立部署槽已正式上线：Launcher API `0.32.0`、owl5
> ServerControlAgent `0.7.0` 与 Velocity Authorizer `0.5.0` 支持从固定 `activity`
> 安全模板创建生存、活动、PVP、小游戏槽。动态槽使用 `25600-25611` 中的独立端口、
> 独立 Velocity 目标且不属于活动冲突组，因此可与其他槽同时运行；只有固定
> `activity / 25568 / owl5-activity-slot` 的替换服仍互斥。现有工业季已迁移为停止的
> `Survival / 25600 / activity-survival` 独立生存槽，固定活动服 PID 和监听未变化。
> 发布记录见 [`docs/API_RELEASE_0.32.0.md`](docs/API_RELEASE_0.32.0.md)、
> [`docs/SERVER_CONTROL_AGENT_RELEASE_0.7.0.md`](docs/SERVER_CONTROL_AGENT_RELEASE_0.7.0.md)
> 与 [`docs/VELOCITY_AUTHORIZER_RELEASE_0.5.0.md`](docs/VELOCITY_AUTHORIZER_RELEASE_0.5.0.md)。

> `2026-08-14` Launcher API `0.30.7` 已将活动客户端下载与进服权限分离。已登录玩家
> 可以看到可见活动并提前下载签名客户端；`canJoin` 独立反映最低称号和单服
> `Allow` / `Deny`，永久服继续隐藏无权记录，Velocity 保留最终门禁。生产真实认证
> 探针与完整解决方案 `748/748` 通过，发布只重启 API，没有操作游戏服或代理。发布记录
> 见 [`docs/API_RELEASE_0.30.7.md`](docs/API_RELEASE_0.30.7.md)。

> `2026-08-14` Launcher API `0.30.4` 已修复 Publisher 因日志占满磁盘而长期等待工作空间的
> 问题：ASP.NET Core 逐请求日志降为 `Warning`，journald 限制为 `1 GiB` 并保留至少
> `8 GiB` 文件系统空间。受影响的整合包任务已自动恢复并完成，50 次健康请求未产生
> syslog 增量。API `313/313`、完整解决方案 `733/733`、Vitest `11/11`、Playwright
> `26/26` 通过；发布只重启 API，没有操作 Publisher、Nginx 或 Minecraft 服务。
> 发布记录见 [`docs/API_RELEASE_0.30.4.md`](docs/API_RELEASE_0.30.4.md)。

> `2026-08-14` LuckPerms 等级回退已完成生产修复：历史
> `Lobby-PvpReturn-Staging` 的 `0.1.0` 实例曾与正式大厅共用 `agent-id=owl5-lobby` 并
> 竞争真实命令，现已禁用和停止。正式大厅已加载 Tier Agent `0.1.3`，API 已升级到
> `0.30.3`，以软件版本和协议 `2` 拒绝旧实例。两条正常业务 `vip` 变更跨四个五分钟
> 间隔未回退，117 条快照、身份映射和用户等级差异均为 `0`；部署与验收脚本没有创建、
> 重放或修改任何玩家身份。
> 发布记录见 [`docs/LUCKPERMS_TIER_AGENT_RELEASE_0.1.3.md`](docs/LUCKPERMS_TIER_AGENT_RELEASE_0.1.3.md)
> 与 [`docs/API_RELEASE_0.30.3.md`](docs/API_RELEASE_0.30.3.md)。

> `2026-08-11` Launcher API `0.30.2` 已上线成员问卷正版资格端点。官网只能通过
> 回环论坛桥接读取账号启用状态和已验证 Minecraft 身份；端点只读、故障关闭，不创建账号
> 或授予游戏权限。API `305/305`、完整解决方案 `725/725` 和生产 29 个映射账号聚合探针
> 通过。发布记录见 [`docs/API_RELEASE_0.30.2.md`](docs/API_RELEASE_0.30.2.md)。

> `2026-08-11` Launcher API `0.30.1` 已修复生产 CSP 下“活动企划”无法打开的问题。
> FullCalendar 现在复用经过 SHA-256 精确授权的样式锚点，CSP 仍不允许
> `'unsafe-inline'`。完整解决方案 `722/722`、Vitest `11/11`、Playwright `26/26`
> 及真实已登录生产后台点击均通过。发布记录见
> [`docs/API_RELEASE_0.30.1.md`](docs/API_RELEASE_0.30.1.md)。

> `2026-08-10` 活动企划与单活动槽已正式上线：Launcher API `0.30.0`、owl5
> ServerControlAgent `0.5.0` 和官网后台现在管理同一组 PostgreSQL 企划；已发布排期使用
> `[开始, 结束)` 全局排斥约束，玩家可提前下载绑定客户端，但只有开放窗口、活动服在线、
> 代理新鲜且部署身份完全一致时才能进服。`.NET 719/719`、Vitest `11/11`、Playwright
> `25/25`、迁移 `28/28` 和生产冲突测试通过，活动槽保持停服。发布记录见
> [`docs/API_RELEASE_0.30.0.md`](docs/API_RELEASE_0.30.0.md) 与
> [`docs/SERVER_CONTROL_AGENT_RELEASE_0.5.0.md`](docs/SERVER_CONTROL_AGENT_RELEASE_0.5.0.md)。

> `2026-08-17` 启动器 `0.15.9` 已正式发布：客户端更新会先复用当前文件和对象缓存，
> 只下载真正缺失的唯一对象；本地 SHA-256 检查、增量下载、客户端准备和版本切换已
> 分阶段显示，不再把整包校验量误报成网络下载量。完整解决方案 `806` 项通过、`1` 项
> 环境集成测试跳过，`0.15.8 -> 0.15.9` 安装生命周期、私有 OSS 双轮回读和认证更新
> 计划均通过。正式记录见
> [`docs/LAUNCHER_RELEASE_0.15.9.md`](docs/LAUNCHER_RELEASE_0.15.9.md)。
>
> `2026-08-10` 启动器 `0.15.7` 已正式发布：活动页进入时立即刷新并每 30 秒同步，
> 离开后停止轮询；跨午夜企划按 `[开始, 结束)` 精确映射，侧栏账户卡移除重复操作。
> Launcher 后台、官网后台、Launcher 活动页和官网公开日历继续读取 API `0.30.4`
> 的同一组 PostgreSQL 企划，活动客户端只允许在 Launcher 内下载。完整解决方案
> `721/721`、Launcher `227/227`、私有 OSS 双轮回读和 `0.15.6 -> 0.15.7`
> 安装生命周期通过。正式记录见
> [`docs/LAUNCHER_RELEASE_0.15.7.md`](docs/LAUNCHER_RELEASE_0.15.7.md)。

> `2026-08-10` 启动器 `0.15.6` 已正式发布：当前服务器详情改为上下锚定，标题、说明、
> 状态和分类贴近横幅顶部，主操作与三点菜单稳定贴近横幅底部；主按钮收紧为
> `148 x 40`，不再随高窗口漂移。完整解决方案 `710/710`、Launcher `225/225`、
> 四档真实 WPF 截图、`0.15.5 -> 0.15.6` 隔离升级、私有 OSS 双轮回读和真实登录态
> 更新链均通过。正式记录见
> [`docs/LAUNCHER_RELEASE_0.15.6.md`](docs/LAUNCHER_RELEASE_0.15.6.md)。

> `2026-08-10` 启动器 `0.15.5` 已正式发布：移除侧栏社区声明，统一账户卡、服务器目录
> 与快捷设置底边及目录两侧 `14px` 留白，并用弹性比例填满高窗口业务区。完整解决方案
> `710/710`、Launcher `225/225`、Publisher `55/55`、三档真实 WPF 截图、
> `0.15.4 -> 0.15.5` 隔离升级、私有 OSS 双轮回读和真实登录态更新链均通过。正式记录见
> [`docs/LAUNCHER_RELEASE_0.15.5.md`](docs/LAUNCHER_RELEASE_0.15.5.md)。

> `2026-08-09` 启动器 `0.15.4` 已正式发布：主页按照确认参考图完成整页布局收口，顶部
> 栏、紧凑导航、服务器目录、当前服务器顶卡、公告/活动分栏、快捷设置和左下账户面板
> 统一到同一组基线与密度。完整解决方案 `710/710`、Launcher `225/225`、Publisher
> `55/55`、Release 零警告零错误、三种窗口真实 WPF 截图、`0.15.3 -> 0.15.4` 隔离
> 升级、私有 OSS 双轮回读和真实登录态更新链均通过。正式记录见
> [`docs/LAUNCHER_RELEASE_0.15.4.md`](docs/LAUNCHER_RELEASE_0.15.4.md)。

> `2026-08-09` 启动器 `0.15.3` 已正式发布：统一修复主页内存下拉弹层的异常阴影、
> 默认虚线焦点框和“查看活动”按钮底边裁切；账号信息从服务器目录移到最左侧导航栏
> 底部，服务器横幅、详情与主操作合并为一个连续主卡片。功能提交 `2b00f10`、
> `5ff4d59` 已通过启动器 `225/225`、完整解决方案 `710/710`、Release 零警告零错误、
> 宽屏与最小窗口 WPF 截图、`0.15.2 -> 0.15.3` 隔离升级、私有 OSS 双轮回读和真实
> 登录态更新链。正式记录见
> [`docs/LAUNCHER_RELEASE_0.15.3.md`](docs/LAUNCHER_RELEASE_0.15.3.md)。

> `2026-08-09` 启动器 `0.15.2` 已正式发布：主页移除下方客户端详情区，把回滚、修复、
> 删除、设置和 Java 模式集中到主按钮右侧的三点菜单；快捷设置改为两行当前档案控制栏，
> 游戏目录显示并打开真实的 `instances\<profile-id>\.minecraft`，全局数据根目录只在
> 设置页修改。Release 构建为 `0` 警告、`0` 错误，完整解决方案 `708/708`；私有 OSS
> 双轮回读、`0.15.1 -> 0.15.2` 隔离升级和真实登录态更新链均已通过。正式记录见
> [`docs/LAUNCHER_RELEASE_0.15.2.md`](docs/LAUNCHER_RELEASE_0.15.2.md)。

> `2026-08-08` 启动器 `0.15.1` 已正式发布：左侧宽导航、中间真实服务器目录、右侧当前
> 服务器主视区、真实公告与近期活动、Java/内存/目录快捷设置已按参考布局落地；该版本
> 同时包含官网同源活动月历。私有 OSS、隔离升级和真实登录态更新链均已验收，记录见
> [`docs/LAUNCHER_RELEASE_0.15.1.md`](docs/LAUNCHER_RELEASE_0.15.1.md)。

> `2026-08-08` 启动器 `0.15.0` 已正式发布：“活动”页现在使用与官网同一 Launcher
> API 排期的固定六周月历，支持月份切换、今天、跨日活动、待排期和日期详情；零活动时
> 仍显示完整月历。私有 OSS 首次上传、重复发布跳过、两轮签名/匿名读取、隔离安装和
> 真实登录态 `0.14.2 -> 0.15.0` 更新链均通过。证据见
> [`docs/LAUNCHER_RELEASE_0.15.0.md`](docs/LAUNCHER_RELEASE_0.15.0.md)。

> `2026-08-07` 管理后台抽屉布局修复：生产 API `0.28.7` 已修复服务器新增/编辑和
> 单服权限等表单型抽屉被压缩到顶部的问题。Playwright `18/18`、API `289/289`、
> 完整解决方案 `695/695` 通过；发布只重启 API，没有操作 Publisher、Nginx、
> Minecraft、Velocity 或服控代理。证据见
> [`docs/API_RELEASE_0.28.7.md`](docs/API_RELEASE_0.28.7.md)。

> `2026-08-06` 活动槽内存校验修复：生产 API `0.28.4` 已在已删除固定活动目录且
> `settings=null` 时提供受控 `4096 MiB` 部署上限，管理后台、确认接口与部署编排使用
> 同一规则。“发布并部署”不再被误判为超过 `0 MiB`，其他缺失设置目标仍保持拒绝。
> 完整解决方案 `673/673`。证据见 [`docs/API_RELEASE_0.28.4.md`](docs/API_RELEASE_0.28.4.md)。

> `2026-08-06` 活动槽重新部署修复：生产 API `0.28.3` 允许整合包页面读取已删除目录但
> 仍保留部署能力的 `activity` 目标，不再误报服控代理离线；普通服控列表仍只显示现存
> 目录目标。生产为 9 个总目标、普通概览 6 个、整合包概览 9 个，完整解决方案
> `671/671`。证据见 [`docs/API_RELEASE_0.28.3.md`](docs/API_RELEASE_0.28.3.md)。

> `2026-08-06` 整合包确认输入修复：生产 API `0.28.2` 已把“精确确认”文本纳入
> 本地草稿快照，后台每 3 秒刷新任务状态时不再清空正在输入的内容；脏数据提示和提交
> 按钮状态也会保持。Playwright `16/16`、API `282/282` 和完整解决方案 `670/670`
> 通过，发布没有向任何游戏服或代理发送控制命令。证据见
> [`docs/API_RELEASE_0.28.2.md`](docs/API_RELEASE_0.28.2.md)。

> `2026-08-06` 一次性服务端清理上线：生产 API `0.28.0` 与 owl5/owl9
> ServerControlAgent `0.4.0` 已提供受控的“删除服务端文件”操作。只有显式白名单中的
> 已停止目标可删除，管理员必须输入 `DELETE <serverId>` 并填写原因；后台目标、外置备份、
> OSS 客户端和计划任务均保留。本次发布没有执行真实删除，也没有启停 Minecraft 或
> Velocity。发布与生产证据见 [`docs/API_RELEASE_0.28.0.md`](docs/API_RELEASE_0.28.0.md)、
> [`docs/SERVER_CONTROL_AGENT_RELEASE_0.4.0.md`](docs/SERVER_CONTROL_AGENT_RELEASE_0.4.0.md)
> 和 [`docs/evidence/SERVER_DIRECTORY_DELETION_PRODUCTION_DEPLOYMENT_2026-08-06.json`](docs/evidence/SERVER_DIRECTORY_DELETION_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

> `2026-08-06` 整合包识别修复：生产 API `0.27.3` 已支持自动识别任意命名的
> 客户端/服务端顶层目录，并修正阻断状态文案。真实 1.12.2 Forge 包已识别为客户端
> 4,355 文件、服务端 2,303 文件、阻断项 0，当前保持等待确认，未发布或部署。证据见
> [`docs/API_RELEASE_0.27.3.md`](docs/API_RELEASE_0.27.3.md)。

> `2026-08-06` 管理后台按钮修复：生产 API `0.27.2` 已修复禁用按钮在鼠标移入时
> 背景变白、文字和图标不可见的问题。API `274/274`、Vitest `8/8`、Playwright
> `14/14` 通过，发布未操作任何 Minecraft 服务端。发布证据见
> [`docs/API_RELEASE_0.27.2.md`](docs/API_RELEASE_0.27.2.md)。

> `2026-08-05` 官网联动更新：生产 API `0.27.1` 已提供严格脱敏的公开活动投影、
> 启动器正式版元数据和短期 HTTPS 下载重定向；官网日程、ICS 与 `/download` 已接入。
> 当次可下载启动器仍为不可变正式版 `0.14.2`，该 API 发布没有覆盖 OSS 安装包。发布证据见
> [`docs/API_RELEASE_0.27.1.md`](docs/API_RELEASE_0.27.1.md)。

> `2026-08-05` Publisher Agent `1.1.0` 已从管理员 Windows 电脑迁移到生产 API
> 所在阿里云，由 `hechao-package-publisher.service` 以独立无登录账号和
> `systemd-credentials` 常驻运行。Windows 计划任务已停止但完整保留为回滚实例；迁移
> 期间未升级 API、未修改 OSS 对象，也未启停 Minecraft、Velocity 或 owl5 服务端。
> 发布与验收见 [`docs/PUBLISHER_RELEASE_1.1.0.md`](docs/PUBLISHER_RELEASE_1.1.0.md)。

> `2026-08-05` 整合包导入正式上线：生产 API `0.27.0`、Publisher Agent `1.0.0`
> 与 owl5 ServerControlAgent `0.3.1` 已完成固定试包。客户端只进入私有 OSS `Test`
> 通道，Gray/Production 未变化；服务端在停止的活动槽完成原子部署后，原活动服又按
> 受控回滚目录逐文件复核并恢复，最终仍为停服。Velocity 路由与五个 Minecraft Java
> PID 全程未变化。操作边界与证据见
> [`docs/PACKAGE_IMPORT_OPERATIONS.md`](docs/PACKAGE_IMPORT_OPERATIONS.md)、
> [`docs/API_RELEASE_0.27.0.md`](docs/API_RELEASE_0.27.0.md)、
> [`docs/PUBLISHER_RELEASE_1.0.0.md`](docs/PUBLISHER_RELEASE_1.0.0.md) 和
> [`docs/SERVER_CONTROL_AGENT_RELEASE_0.3.1.md`](docs/SERVER_CONTROL_AGENT_RELEASE_0.3.1.md)。

> `2026-08-02` 管理后台更新：生产 API 已升级到 `0.26.2`。九个 Vue 管理模块保留
> `0.26.1` 的票据预路由清理，并修复短窗口和长页面无法完整滚动、侧栏项目不可达的问题；
> TypeScript、Vitest `8/8`、Playwright `12/12` 和完整解决方案 `578/578` 通过。发布证据见
> [`docs/API_RELEASE_0.26.2.md`](docs/API_RELEASE_0.26.2.md)。
>
> `2026-08-01` 更新：启动器当前正式版本为 `0.14.2`，生产启动检查与自动更新通道已启用；
> 系统代理可在设置中按需开启，默认保持直连。发布证据见
> [`docs/LAUNCHER_RELEASE_0.14.2.md`](docs/LAUNCHER_RELEASE_0.14.2.md)。
> 生产 API 已用真实会话确认 `0.14.1 -> 0.14.2` 更新计划与安装包下载；正式安装进程将在玩家下次启动时自动升级。

> `2026-08-01` 前端布局、无障碍与客户端业务逻辑审查已随 `0.14.2` 正式发布，
> 完整解决方案 `564/564` 通过。审查记录见
> [`docs/LAUNCHER_FRONTEND_AUDIT_2026-08-01.md`](docs/LAUNCHER_FRONTEND_AUDIT_2026-08-01.md)。

> `2026-08-02` 服控更新：owl5 已部署 ServerControlAgent `0.2.4`，在 `0.2.3` 的
> stdout 管道修复上继续保证本机日志失败和未知轮询异常不会终止代理；owl9 保持
> `0.2.1`。发布证据见
> [`docs/SERVER_CONTROL_AGENT_RELEASE_0.2.4.md`](docs/SERVER_CONTROL_AGENT_RELEASE_0.2.4.md)。

> `2026-08-01` owl9 状态采集器升级到 `0.2.2`：共享 `25565` 的
> `pvp`（恐怖整蛊）与 `pvp-purpur`（真正 PVP）改为按监听 PID 的 Java 路径
> 归属，真正 PVP 的进程、玩家、CPU、内存和磁盘已正常上报。发布证据见
> [`docs/STATUS_COLLECTOR_RELEASE_0.2.2.md`](docs/STATUS_COLLECTOR_RELEASE_0.2.2.md)。

赫朝 Minecraft 社区的 Windows 桌面启动器。当前生产为启动器 `0.15.9`、API `0.34.0`、LuckPerms Tier Agent `0.1.3`、Publisher Agent `1.2.1`、owl5/owl9 ServerControlAgent `0.7.2`、Velocity Authorizer `0.5.0`（`monitor`）和 Lobby Guard `0.1.0`；可回滚部署、独立生存/活动/PVP/小游戏槽、全权限游戏控制台、固定整合包、双后台企划日历、单活动排期、官网与启动器同源活动月历、三栏服务器主页、公开下载桥接、成员问卷正版资格桥接与一次性服务端文件清理验收均已完成，剩余门槛只涉及真实四级账号完整路径、真实活动玩法包、QQ 测试群审批验收和 `2/3/5/20` 人逐级灰度。平台已经完成 C 版响应式视觉系统、启动时自动检查并安装启动器更新、客户端档案删除、跨档案玩家设置共享、运行中服控目标自动发现、赫朝账号、Microsoft/Minecraft 正版绑定、HTTPS 服务器目录、LuckPerms 等级同步与受控修改、权限过滤、活动预下载与进服门禁分离、签名客户端分发、按哈希增量更新、平滑并行断点续传、SHA-256 校验、修复、主动回滚、原子版本切换、每档案独立 `.minecraft`、共享下载对象、每档案受管 Java 与自定义 Java、Windows 安装包、真实 Minecraft 启动、本地脱敏诊断及玩家确认上传、隐私受限运行遥测、Velocity 服务端二次授权、只读实时状态与进程指标采集、统一告警，以及带独立浏览器会话、双重验证、活动排期、经济监控与单品 K 线、玩家搜索、单服权限规则、论坛会话联动、账号安全操作、整合包导入和受控服控的 Vue 管理控制台。

2026-07-29 已确认新架构：赫朝启动器成为唯一服务器选择和切换入口；大厅继续作为 LuckPerms 等前置能力的内部承载器，但不再向玩家展示、授权、路由或回退。Velocity 继续负责统一公网入口、forwarding 和服务端二次授权。完整约束、回滚和验收标准见 [`docs/LAUNCHER_ONLY_SERVER_SWITCHING.md`](docs/LAUNCHER_ONLY_SERVER_SWITCHING.md)。

由赫朝独立运营。非 Minecraft 官方产品。未经 Mojang 或 Microsoft 批准，也不与 Mojang 或 Microsoft 关联。

## 当前能力

- 展示生存服和活动服的状态、在线人数、核心与 Minecraft 版本；基础设施大厅只在管理员运维视图中可见，不进入玩家目录。
- 提供服务器、下载、活动、赫朝账户和设置五个真实工作区；短屏与高 DPI 下使用受约束布局和局部滚动，不裁切运行参数。
- 活动工作区显示与官网同源的固定六周月历；支持月份切换、今天、跨日活动、待排期、
  日期详情和活动客户端下载，零活动时仍保留完整日历结构。打开活动页会立即同步目录，
  停留期间每 `30` 秒刷新；官网公开日历只展示企划时间，活动客户端只能在启动器内下载。
- 使用 IconPark 官方轮廓图标统一功能按钮与状态图形；界面优先使用系统已安装的苹方字体，并在不可用时回退到微软雅黑。
- 先注册或登录赫朝账号，再独立绑定 Microsoft/Minecraft Java 正版身份；旧版 Microsoft 临时账户可在验证同一正版身份后安全并入正式赫朝账号。
- 已绑定 Minecraft 身份时在左下账号区域显示 Mojang 官方皮肤的头部与帽层；网络、
  缓存或图片异常时回退本地默认头像。
- 由启动器独占服务器切换，根据在线/维护状态控制主操作；已有游戏运行时先安全退出，再使用新授权启动目标档案。
- 读取经 ECDSA P-256 签名的客户端清单；未知公钥、篡改负载、危险路径和远程明文 HTTP 会被拒绝。
- 使用最多 16 路受控并行、HTTP Range 断点续传和 SHA-256 逐文件校验；重复摘要只下载一次，下载失败时保留 `.part` 供下次继续。
- 在独立暂存目录构建完整客户端，通过目录重命名切换活动版本，并保留一个 `.previous` 版本供回滚。
- 玩家可在退出对应 Minecraft 后主动回滚到上一版本；启动器会使用同一跨进程锁、硬链接优先的独立暂存副本和原子目录交换，并把当前存档、截图、设置与服务器列表带入回滚版本。
- 使用安装式启动器和独立游戏数据根目录；每个客户端档案拥有自己的 `instances\<profile-id>\.minecraft` 与 `runtime`，下载对象跨档案共享，Java 默认随对应档案安装并允许单独改为玩家选择的兼容运行时。Windows 特殊字符路径会为游戏工作目录、Java 和原生库分别选择兼容路径，不移动或复制玩家档案。
- 首次运行自动迁移旧 `%AppData%\Hechao\instances` 或自定义客户端根目录；迁移失败时保留原目录并停止启动，不静默切换到空数据。
- Windows 安装包按当前用户安装到 `%LocalAppData%\Programs\Hechao Launcher`；升级和卸载均保留游戏数据。
- 启动器本体自更新已上线：校验 API 发布元数据和私有 OSS 安装包后，由
  临时更新器静默覆盖并重新拉起；失败保留原版本。首个正式自更新版本发布后，玩家
  后续常规升级无需重复下载安装包。
- 修复流程会重新检查本地文件；同档案的并发安装通过跨进程独占锁阻止。
- 提供实时下载任务、持久化历史、取消任务、活动服目录、客户端修复入口和完整设置页。
- “启动时检查客户端更新”可关闭首次本地扫描，但进入服务器前仍强制检查；重新开启时立即检查当前档案。
- 将所选服务器、内存、游戏数据目录、默认页面、缓存与启动行为保存到 `%LocalAppData%\Hechao\Launcher\settings.json`。
- 通过 `IServerCatalogClient` 从 HTTPS API 读取服务器目录，并按“在线 API、上次成功缓存、内置应急目录”顺序降级。
- 使用赫朝账号建立社区会话；绑定游戏身份时使用系统浏览器执行 Microsoft OAuth 与 PKCE，再通过 Xbox/XSTS/Minecraft 验证 Java 正版权益。
- 进入服务器时优先静默续期 Minecraft 游戏会话；缓存无法续期时自动打开系统浏览器刷新 Microsoft 凭据，校验所选账号与已绑定 Minecraft UUID 一致后继续启动，不要求退出赫朝账号。
- 使用 15 分钟访问令牌和可撤销、轮换的刷新令牌；刷新会话由 Windows DPAPI 保护。
- 账户页支持退出当前设备、原子撤销全部设备及后台会话，并在校验当前赫朝密码后解除 Minecraft 身份绑定；解除绑定会撤销全部会话和待使用进服授权，并把等级回退为 `Member`。
- 从共享 LuckPerms 数据库每 5 分钟同步主组，按 `Member`、`Participant`、`Collaborator`、`Administrator` 过滤目录。
- 私有 OSS 下载通过启动器 API 鉴权；API 仅为清单内对象签发 5 分钟 V4 URL，Bearer 不会随跳转发送到 OSS。
- 生产发布公钥已内嵌，启动器只信任 `release-2026-07-primary`；私钥使用 Windows DPAPI 加密离线保存，并已通过 RSA/AES-GCM 加密恢复包完成真实恢复和验签演练。
- 使用每档案受管 Java 运行时和签名档案构建正版会话，直接连接 `mc.hehe11.fun`；基础 Fabric、纯 Vanilla、Forge `47.4.0`、NeoForge `21.11.42`、恐怖整蛊 Fabric（历史档案 ID 为 `pvp-fabric-1.20.1`）和 DollNight 六套档案均已正式发布。
- 记录 Minecraft 正常或异常退出；玩家可在设置页主动生成脱敏、限大小的本地诊断包，世界存档和账号凭据不会进入 ZIP，文件不会自动上传。
- 在 Minecraft 进程启动前申请 10 分钟、一次性 Velocity 启动授权；授权失败时不会创建游戏进程。
- Velocity 插件异步校验正版 UUID、账号状态、服务器状态、LuckPerms 等级和单服例外规则，支持 `disabled`、`monitor`、`enforce` 三种模式；首次连接只接受一次性启动授权指定的目标，不再依赖或回退到游戏大厅。
- Windows 只读采集器每分钟查询各 Velocity 目标，并可按本机监听端口读取 Java 进程内存、CPU、启动时间和磁盘余量；Paper/Purpur 指标代理只把 TPS、MSPT 与累计 GC 时间原子写入本地 JSON。两者都不持有 RCON、控制台或服务器启停权限。
- `Administrator` 可从启动器申请 90 秒一次性后台票据；票据只放 URL fragment，兑换后改用 `HttpOnly`、`Secure`、`SameSite=Strict` 的独立浏览器会话，不把启动器 Bearer 交给网页。
- 管理后台强制 TOTP 双重验证，提供一次性恢复码和 CSRF 防护；支持服务器新增、编辑、归档、恢复、公告、开放排期、玩家搜索、访问预览和单服规则，所有变更使用修订号并在同一事务中写入审计日志。
- 管理后台已在生产迁移到 Vue 3、TypeScript、Vite 和 Vue Router；九个管理模块按路由拆分并按需加载，ASP.NET Core 构建和发布会自动生成 `wwwroot/admin` 静态产物。生产真实票据、九个深层路由、稳定数据态、零横向溢出和零浏览器 warning/error 已验收；`0.26.2` 进一步补齐长正文、短窗口和侧栏导航滚动边界。
- `0.27.0` 已正式部署第十个“整合包导入”模块：8 MiB 分块续传、ZIP/MRPACK
  安全识别、人工复核、独立 Publisher Agent、客户端 `Test` 发布和 owl5 活动槽停服
  原子部署均通过固定试包；系统不会自动开服或推进 Gray/Production。
- `0.32.0` 将受控多槽改为真正独立的部署槽：后台可按生存、活动、PVP、小游戏创建
  `survival-*`、`activity-*`、`pvp-*`、`minigame-*`。API 为每个槽分配独立端口和
  Velocity 目标，代理创建独立目录、主机固定文件快照与无触发器计划任务；动态槽没有
  冲突组，可以同时运行。固定活动替换服继续共享 `activity / 25568 / owl5-activity-slot`。
- 服控面板和最小权限 Windows 代理已接入生产：支持优雅启停、冲突服先停后启、
  `server.properties` 与 JVM 内存快捷设置，以及受限 Minecraft 控制台。当前登记
  9 个受管目标；代理在线数和运行中实例数来自实时心跳，执行动作前必须重新核验。
  内存设置下次启动生效，不会自动重启服务端。
- 管理员可删除不再使用的一次性服务端运行目录；操作仅对白名单目标开放，要求目标已停止、
  无进行中命令、精确二次确认和原因审计。代理只删除已配置的 `serverDirectory`，保留
  外置备份、OSS 客户端、代理配置、计划任务和后台目标记录，且不会自动启停任何游戏服。
- 管理后台可排队四个固定 LuckPerms 全局组的等级变更；大厅代理通过 LuckPerms API 应用，不直接写 MariaDB，也不接受任意控制台命令。
- 全部认证状态撤销和 UUID 封禁会通过可靠 outbox 联动论坛 `sessionVersion`，使已经签发的论坛 Cookie 失效。
- 启动器 API 生产版本 `0.28.4` 已通过 `https://launcher-api.hechao.world` 上线；玩家服务器与内部基础设施角色已拆分，大厅隐藏后仍保留监控，Vue 管理后台支持从真实运行状态发现服务器、导入整合包和受控删除停用服务端文件。整合包精确确认文本在 3 秒轮询期间保持；已删除固定活动槽在快速设置为空时使用受控 `4096 MiB` 部署上限，其他缺失设置目标仍保持拒绝。公开活动与启动器下载投影使用独立匿名限流，对象签名入口、登录与全局防刷限制保持分离。
- API 私有对象重定向不会把短时 OSS 签名 URL 写入 journal；Nginx 访问日志只保留无查询参数的路径，不记录 Referer，避免密码重置和 OAuth 参数进入日志。
- API 每分钟评估 5xx、延迟、登录失败、下载失败和服务器运行状态；独立监控器检查公网入口、私有 OSS 基线、TLS 证书与异地备份状态，只在新告警、级别变化和恢复时发送邮件，不控制游戏服进程。

API `0.22.0-20260729T144953Z` 于 2026-07-29 首次完成一致性备份、哈希校验、迁移 `019`、原子切换、公网回归和大厅基础设施角色验收；这些能力现由 `0.28.4-20260806T002900Z` 继续承载，迁移为 `024`，`/healthz` 与 `/readyz` 当前均正常，公开目录对 `lobby` 为零命中。账号安全、论坛 Cookie 联动、客户端三通道、隐私受限遥测、服务器运行指标、统一告警、整合包导入和服务端文件清理均在线。Nginx 五个站点入口已启用无查询参数、无 Referer 的访问日志，合成重置 token 回归泄漏数为 `0`。状态采集器 `0.2.1` 与三类指标代理已实时上报大厅、Survival1、Survival2、Activity 和恐怖整蛊（历史目标 `pvp`）的进程、磁盘、TPS、MSPT 与累计 GC；Activity 零玩家时的 NeoForge 暂停会显式显示为空服暂停，不再误报指标过期。当前仅完成单用户空载基线，不替代多人负载验收。大厅 LuckPerms 等级代理、Lobby Guard `0.1.0` 和指标代理均已加载。生产 Velocity Authorizer `0.4.0` 保持 `monitor`，所有首次连接故障硬拒绝并永久拒绝基础设施目标。当前验证为完整解决方案 `673/673`、API `285/285`、ServerControlAgent `51/51`、Vitest `8/8` 和 Playwright `16/16`。

API `0.28.4` 与 owl5、owl9 服控代理 `0.4.0` 已在生产启用每服 JVM 内存展示、受控修改、目录实际状态同步、运行中服务器发现、停止活动槽的整合包部署和白名单服务端文件删除。数据库保留全部 9 个目标及其审计记录，普通服控概览当前显示 6 个存在目录的目标，整合包专用概览可读取全部 9 个；`activity`、`fanstreet` 和 `yugong` 已完成删除和后台清理，因此不再占用日常列表，重新部署目录并恢复心跳后会自动出现。服控心跳与命令执行使用独立、可恢复循环，长停止命令、本机日志失败或单次未知异常不会使整台 VPS 停止上报。当前只有 `dollnight`、`activity`、`fanstreet`、`yugong` 和历史 ID `pvp` 可删除；`lobby`、`survival1`、`survival2` 与真正 PVP `pvp-purpur` 保持禁止。发布与内存基线见 [`docs/API_RELEASE_0.24.0.md`](docs/API_RELEASE_0.24.0.md)，目录同步见 [`docs/API_RELEASE_0.24.1.md`](docs/API_RELEASE_0.24.1.md)，服务器发现见 [`docs/API_RELEASE_0.24.2.md`](docs/API_RELEASE_0.24.2.md)，当前显示规则见 [`docs/API_RELEASE_0.28.1.md`](docs/API_RELEASE_0.28.1.md)，轮询输入修复见 [`docs/API_RELEASE_0.28.2.md`](docs/API_RELEASE_0.28.2.md)，活动槽重新部署修复见 [`docs/API_RELEASE_0.28.3.md`](docs/API_RELEASE_0.28.3.md)，空设置内存校验修复见 [`docs/API_RELEASE_0.28.4.md`](docs/API_RELEASE_0.28.4.md)，删除能力见 [`docs/API_RELEASE_0.28.0.md`](docs/API_RELEASE_0.28.0.md) 与 [`docs/SERVER_CONTROL_AGENT_RELEASE_0.4.0.md`](docs/SERVER_CONTROL_AGENT_RELEASE_0.4.0.md)。

真实管理员已完成 MFA 登记，`0.11.14` 已产生首条真实启动遥测，诊断上传、管理员下载、审计和本地 SHA-256 复验均已完成。基础客户端的 Lobby、Survival1、Survival2、Activity 与恐怖整蛊历史单账号首次路由均已通过；恐怖整蛊的 CrossStitch 修复、身份转发、直连拒绝、稳定连接和正常退出也已验收。Activity 在含 U+200C 的既有数据根目录下已由 `0.12.3` 改用 `%LocalAppData%\Hechao\Launcher\native-runs` 物理目录：`java.library.path`、`org.lwjgl.librarypath`、JNA、LWJGL 解压和 Netty 五个属性唯一指向该目录，不再依赖可能被 Windows 原生加载器解析回真实目标的目录联接。安装版启动器从“进入服务器”完成正版会话、连接 `mc.hehe11.fun`、进入 Activity 世界并以退出码 `0` 正常结束，全程未复现 `UnsatisfiedLinkError` 或 `Can't find dependent libraries`。同档案三轮 fresh grant 重进、NeoForge/Paper 跨档案三轮切换、15 分钟单进程采样、启动器重启接管、强制异常退出和新授权恢复也已用同一真实账号通过，全程未出现 Lobby 回退；Activity 运行时选择维护中的 DollNight 或已关闭的 Survival1，主操作均禁用且现有 PID 不变。跨版本回大厅曾在 API `0.21.0` 和 Velocity 4 隔离环境完成五轮真实客户端验证，相关证据仅保留用于审计；2026-07-29 的新架构已经取消 `/hub`、NPC 和 Via 回大厅方案。生产代理已迁移至 Velocity 4、独立 Java 25 和 Authorizer `0.4.0` monitor；API `0.22.0`、Lobby Guard `0.1.0`、旧回程移除及后端 `/hub` 禁用均已落地。大厅八个玩家交互 Skript 已在线禁用并保留哈希备份，只留每日备份；公网 `25566` 不可达，owl5 与 owl9 恐怖整蛊均无活动的旧转服路径。下一步只按 [`docs/PRELAUNCH_PILOT_0.12.3.md`](docs/PRELAUNCH_PILOT_0.12.3.md) 完成真实四级账号、离线/无权限拒绝、`enforce`、目录强制登录和 `2/3/5/20` 人灰度。

三份 Paper 世界正式归档、远端 ZIP/旁车复核、异机完整解压、`level.dat` 校验和确定性区域抽样恢复已通过；owl9 恐怖整蛊另完成正式 VSS 归档、`2,493/2,493` 文件哈希比对和 `2,370/2,370` 区域全量恢复检查，真正 PVP 未触碰。RAM v5 已默认生效；启动器数据库、论坛与 Sub2API 的异地加密链均已完成真实 OSS 往返、定时任务、告警恢复与异地主机隔离恢复。六个活动签名档案的 `8,944` 个去重对象也已建立 OSS 外完整副本，并通过远端全量哈希和隔离恢复。平台监控器 `0.1.2` 已生产运行。客户端不会使用第三方启动器凭据，不采集 Microsoft 密码，也不保存赫朝账号密码。

## 项目结构

- `src/Hechao.Launcher`：WPF 桌面客户端、视图模型、本地设置和演示服务。
- `src/Hechao.Contracts`：服务器目录、客户端档案、权限等级和 API 接口模型。
- `src/Hechao.Distribution`：签名清单、路径策略、断点续传、哈希校验、安装与回滚核心。
- `src/Hechao.Publisher`：管理员离线生成密钥、内容寻址对象和签名清单，并使用 DPAPI 凭据上传 OSS 对象的命令行工具；也包含隔离运行的整合包 Publisher Agent。
- `src/Hechao.Backup.Core`：Publisher 与备份 CLI 共用的 RSA/AES-GCM 加密信封类库，
  不包含可执行入口。
- `src/Hechao.Backup`：数据库与签名恢复材料的 RSA/AES-GCM 加密信封、私有 OSS 不可覆盖上传和下载复验工具。
- `src/Hechao.Api`：独立启动器 API、管理员 Web 控制台、MFA、目录 CRUD 与审计；只监听 `127.0.0.1:8090`，由 Nginx 终止公网 TLS。
- `src/Hechao.Api/AdminWeb`：Vue 3 + TypeScript + Vite 管理后台源码、Vitest 单元测试和 Playwright 浏览器测试；`wwwroot/admin` 只是构建产物。
- `src/Hechao.StatusCollector`：游戏 VPS 上的只读 Minecraft 状态采集器，使用机器级 DPAPI 保护内部令牌。
- `src/Hechao.ServerControlAgent`：游戏 VPS 上的结构化最小权限服控代理；只使用固定
  计划任务、Minecraft 控制台桥和白名单设置字段。
- `src/Hechao.ServerMetricsAgent`：Paper/Purpur 只读 TPS、MSPT 与 GC 本地指标代理。
- `src/Hechao.VelocityAuthorizer`：Velocity 4 / Java 25 生产运行、向下兼容测试环境的异步进服授权插件。
- `src/Hechao.LuckPermsTierAgent`：大厅 Paper / Java 21 受控全局等级代理。
- `src/Hechao.LobbyGuard`：大厅 Paper 后端玩家登录拒绝插件；不修改 LuckPerms、指标或备份。
- `installer`：NSIS 3 简体中文/英文安装脚本。
- `tools/Build-WindowsInstaller.ps1`：测试、发布、安装包编译和 SHA-256 生成入口。
- `tests/Hechao.Distribution.Tests`：签名、路径、续传、跨域令牌隔离、坏哈希、并发锁和原子回滚测试。
- `tests/Hechao.Api.Tests`：目录摘要锚定、OSS V4 预签名 URL 和进服授权规则测试。
- `tests/Hechao.StatusCollector.Tests`：Minecraft 状态协议、失效目标隔离和心跳批次测试。
- `deploy/linux`：阿里云上的 systemd、PostgreSQL、备份、发布脚本和 Nginx 模板，不包含密码或密钥。
- `deploy/windows/luckperms-sync`：游戏 VPS 的只读 LuckPerms 同步桥与计划任务安装脚本。
- `deploy/windows/server-heartbeats`：一分钟只读状态计划任务、配置样例和 DPAPI 令牌保护脚本。
- `deploy/windows/velocity-authorizer`：只备份和安装插件/配置、不重启 Velocity 的部署脚本。
- `deploy/windows/world-backup`：串行、校验、原子完成并按服保留的世界备份引擎及三个服务端包装脚本。

## 本地构建

需要 .NET 10 SDK、Windows 和 PowerShell 7。所有本机发布与 Windows VPS 运维脚本统一使用 `pwsh`，不再使用 Windows PowerShell 5.1；完整版本、安装、任务迁移和回滚规则见 [`docs/POWERSHELL_7_OPERATIONS.md`](docs/POWERSHELL_7_OPERATIONS.md)。构建脚本会优先使用仓库根目录的 `.dotnet\dotnet.exe`，不存在时再使用系统 SDK；本机工具目录不会进入 Git。

```powershell
dotnet build Hechao.Launcher.sln -c Release
dotnet test Hechao.Launcher.sln -c Release
dotnet publish src\Hechao.Launcher\Hechao.Launcher.csproj -c Release -p:PublishProfile=win-x64 -o artifacts\publish\win-x64
.\tools\Build-WindowsInstaller.ps1
dotnet publish src\Hechao.Api\Hechao.Api.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\api-linux-x64
dotnet publish src\Hechao.StatusCollector\Hechao.StatusCollector.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish\status-collector-win-x64
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.VelocityAuthorizer clean test jar --no-daemon
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.ServerMetricsAgent clean test jar --no-daemon
.\src\Hechao.VelocityAuthorizer\gradlew.bat -p src\Hechao.LobbyGuard clean test jar --no-daemon
```

单独开发或验证管理后台：

```powershell
Set-Location src\Hechao.Api\AdminWeb
npm ci
npm run typecheck
npm test
npm run test:e2e
npm run build
```

`dotnet build` 和 `dotnet publish` 会自动执行管理后台依赖恢复与生产构建。不要直接修改
`src\Hechao.Api\wwwroot\admin\assets\admin.js`、分块脚本或 `admin.css`。

## 接入顺序

1. 已使用独立写入 RAM 身份发布 `base-1.21.11` / `1.0.5`、`activity-neoforge-1.21.11` / `1.0.10` 与 `pvp-fabric-1.20.1` / `1.0.0`，并原子激活签名清单、目录记录和实时心跳；活动服保持关闭。
2. [审核已完成] 管理员于 2026-07-26 确认 Minecraft Java API 访问许可已经通过；仍需完成真实账号验收。
3. [已完成] 生产 Authorizer `0.4.0` 已以 `monitor` 模式加载；内部大厅目标、首次故障关闭和授权目标改写均已部署，Lobby Guard 提供后端独立拒绝。
4. 使用普通、VIP、管理员和服主正版账号完成下载、安装、每档案 Java 运行时准备及单服权限验收。
5. 验收通过后把 Velocity 切到 `enforce`，再启用目录强制登录。
6. [已完成] 部署 API `0.22.0`、私有下载与 Nginx 日志脱敏、统一运行告警及状态采集器 `0.2.1`；赫朝账号、对象分发、下载专用限流、授权定向路由、诊断上传、服务器排期、单服访问规则、论坛会话联动、受控全局等级、运行遥测和服务器进程/磁盘指标均已上线。
7. [已完成自动部署] 启动器 `0.12.3`、API `0.22.0`、Authorizer `0.4.0` 与 Lobby Guard `0.1.0` 已生产发布；API 不可达故障关闭与恢复已通过，继续按 [`docs/PRELAUNCH_PILOT_0.12.3.md`](docs/PRELAUNCH_PILOT_0.12.3.md) 完成真实四级账号、离线/无权限拒绝、Lobby 旁路拒绝和多人灰度。

当前工程不包含 VPS 密钥或服务器凭据。服控代理令牌只以每台主机的 DPAPI
`LocalMachine` 密文保存，API 只存 SHA-256；功能默认关闭，不能由玩家端调用。

## 实施文档

活动企划排期、双后台同步、整合包绑定与单活动槽准入见
[`docs/ACTIVITY_PLAN_OPERATIONS.md`](docs/ACTIVITY_PLAN_OPERATIONS.md)。

真实玩家分档采证、Velocity `enforce` 和目录强制登录的失败关闭切换见
[`docs/GRAY_PILOT_AUTHORIZATION_CUTOVER.md`](docs/GRAY_PILOT_AUTHORIZATION_CUTOVER.md)。

活动客户端、服务端、活动槽、档案发布和回滚的统一开发规范见
[`docs/ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md`](docs/ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md)，
新物理后端的基础组件分层、加载器兼容、forwarding、指标和组件计划见
[`docs/HECHAO_NEW_SERVER_BASELINE.md`](docs/HECHAO_NEW_SERVER_BASELINE.md)，
接手 Codex 可从
[`docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md`](docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md)
的轻量案例开始。

需要把完整规范和关键代码参考交给新的开发者或 Codex 时，使用
[`handoff/activity-development/README.md`](handoff/activity-development/README.md)
中的独立交接包。正式交接包必须从干净 Git 提交生成，并立即通过独立验收器：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\New-ActivityDevelopmentHandoff.ps1
pwsh -NoLogo -NoProfile -File .\tools\Test-ActivityDevelopmentHandoff.ps1 `
  -ArchivePath .\artifacts\handoff\<交接包>.zip
```

只需要把“后台整合包导入”的客户端/服务端格式交给制作人员或 Codex 时，使用更小的
[`handoff/package-import-template/README.md`](handoff/package-import-template/README.md)。
它包含严格源目录校验器、规范业务 ZIP 生成器、各加载器启动脚本示例和可独立验收的总
交接包：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Test-HechaoPackageImportSource.ps1 `
  -SourceDirectory <完整双端源目录>
pwsh -NoLogo -NoProfile -File .\tools\New-HechaoPackageImportArchive.ps1 `
  -SourceDirectory <完整双端源目录> `
  -OutputArchive <业务ZIP>
pwsh -NoLogo -NoProfile -File .\tools\New-HechaoPackageImportTemplateHandoff.ps1 `
  -OutputArchive .\artifacts\handoff\<模板交接包>.zip
pwsh -NoLogo -NoProfile -File .\tools\Test-HechaoPackageImportTemplate.ps1 `
  -HandoffArchive .\artifacts\handoff\<模板交接包>.zip
```

功能、生产验收与外部依赖的权威状态见 [`docs/COMPLETION_MATRIX.md`](docs/COMPLETION_MATRIX.md)。owl9 的恐怖整蛊服与真正 PVP 服边界见 [`docs/OWL9_DUAL_BACKEND_OPERATIONS.md`](docs/OWL9_DUAL_BACKEND_OPERATIONS.md)。完整的平台架构、HTTPS 迁移、客户端下载、权限、管理后台和分阶段任务见 [`docs/PLATFORM_PLAN.md`](docs/PLATFORM_PLAN.md)。玩家安装、迁移、修复与隐私说明见 [`docs/PLAYER_INSTALLATION_GUIDE.md`](docs/PLAYER_INSTALLATION_GUIDE.md)，管理员构建、灰度、发布与回滚流程见 [`docs/ADMIN_RELEASE_RUNBOOK.md`](docs/ADMIN_RELEASE_RUNBOOK.md)。Windows 安装包、数据目录、旧版迁移与卸载边界见 [`docs/WINDOWS_INSTALLER_AND_STORAGE.md`](docs/WINDOWS_INSTALLER_AND_STORAGE.md)，PowerShell 7 运行时与计划任务迁移见 [`docs/POWERSHELL_7_OPERATIONS.md`](docs/POWERSHELL_7_OPERATIONS.md)，游戏退出与隐私诊断规则见 [`docs/GAME_DIAGNOSTICS.md`](docs/GAME_DIAGNOSTICS.md)。管理员浏览器登录与 MFA 见 [`docs/ADMIN_WEB_OPERATIONS.md`](docs/ADMIN_WEB_OPERATIONS.md)，账号停用、会话撤销和 UUID 封禁见 [`docs/ADMIN_ACCOUNT_SECURITY_OPERATIONS.md`](docs/ADMIN_ACCOUNT_SECURITY_OPERATIONS.md)，目录 API 边界见 [`docs/ADMIN_CATALOG_OPERATIONS.md`](docs/ADMIN_CATALOG_OPERATIONS.md)。客户端发布与密钥边界见 [`docs/DISTRIBUTION_OPERATIONS.md`](docs/DISTRIBUTION_OPERATIONS.md)。Microsoft/LuckPerms 激活与运维见 [`docs/AUTHENTICATION_OPERATIONS.md`](docs/AUTHENTICATION_OPERATIONS.md)。Velocity 最终授权见 [`docs/VELOCITY_AUTHORIZATION_OPERATIONS.md`](docs/VELOCITY_AUTHORIZATION_OPERATIONS.md)，代理单层协议转换生产切换见 [`docs/PROXY_PROTOCOL_TRANSLATION_PRODUCTION_OPERATIONS.md`](docs/PROXY_PROTOCOL_TRANSLATION_PRODUCTION_OPERATIONS.md)。只读状态采集见 [`docs/SERVER_HEARTBEAT_OPERATIONS.md`](docs/SERVER_HEARTBEAT_OPERATIONS.md)，深度运行指标见 [`docs/SERVER_RUNTIME_METRICS_OPERATIONS.md`](docs/SERVER_RUNTIME_METRICS_OPERATIONS.md)，统一告警见 [`docs/OPERATIONAL_ALERTS.md`](docs/OPERATIONAL_ALERTS.md)，世界备份见 [`docs/WORLD_BACKUP_OPERATIONS.md`](docs/WORLD_BACKUP_OPERATIONS.md)。实时无密码资产基线见 [`docs/ASSET_INVENTORY.md`](docs/ASSET_INVENTORY.md)，API 发布与回滚见 [`docs/API_OPERATIONS.md`](docs/API_OPERATIONS.md)，数据库本机备份见 [`docs/DATABASE_OPERATIONS.md`](docs/DATABASE_OPERATIONS.md)，异地加密备份与恢复见 [`docs/OFFSITE_BACKUP_AND_RECOVERY.md`](docs/OFFSITE_BACKUP_AND_RECOVERY.md)，版本与 Git 规则见 [`docs/RELEASE_AND_GIT_WORKFLOW.md`](docs/RELEASE_AND_GIT_WORKFLOW.md)。

服控部署、冲突编排和失败回滚见
[`docs/SERVER_CONTROL_AGENT_OPERATIONS.md`](docs/SERVER_CONTROL_AGENT_OPERATIONS.md)，
启动器本体更新见
[`docs/LAUNCHER_SELF_UPDATE_OPERATIONS.md`](docs/LAUNCHER_SELF_UPDATE_OPERATIONS.md)。
后台整合包识别、客户端 Test 发布和 owl5 活动槽停服部署见
[`docs/PACKAGE_IMPORT_OPERATIONS.md`](docs/PACKAGE_IMPORT_OPERATIONS.md)。

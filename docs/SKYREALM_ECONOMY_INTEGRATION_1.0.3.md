# 天域远征工业季经济集成 1.0.3

> 本版保留为历史验收记录。当前候选已由 `1.0.4` 取代，见
> [`SKYREALM_ECONOMY_INTEGRATION_1.0.4.md`](SKYREALM_ECONOMY_INTEGRATION_1.0.4.md)。

> 状态：本地候选已完成；未上传后台、未部署 API/Agent、未执行迁移、未注入令牌，
> 未启动或重启任何 Minecraft 服务端。
> 日期：2026-08-14

## 本版变更

- 一键包版本从 `1.0.2` 提升为 `1.0.3`，不覆盖历史包。
- API `0.31.0` 候选允许整合包部署到服控 Agent 显式设置
  `packageDeploymentEnabled=true` 的合法目标，不再把通用导入硬编码为活动槽。
- owl5 Agent `0.6.0` 候选允许同一 Agent 管理多个显式授权目标；`survival2` 候选目录为
  `E:\Survival2`、端口 `25565`、冲突组 `owl5-survival-slot`、最大允许内存 `6144 MiB`。
- 后台存在多个目标时不默认选择。部署必须明确选择 `survival2`，并输入
  `发布并部署 <importId> 到 survival2`；活动企划继续严格固定 `activity`。
- 目录同步使用所选目标 ID 作为 Velocity 目标，长期生存服不会被写成 `activity`。
- 包级校验新增 JAR 内部合同：核对 `/money`、`/pay`、`/sell`、`/shop`、`/heco`、
  `hechao.economy.admin`、核心 class、NeoForge `BOTH` 侧以及精确版本范围。

## 候选制品

| 项目 | 值 |
| --- | --- |
| 路径 | `E:\天域远征工业季-赫朝一键导入-1.0.3.zip` |
| 大小 | `1,585,997,207` 字节 |
| SHA-256 | `562317F6B12B81A6E33B2728398521D6510AF3B3BD9F9DFCEEB23EE31CB7C315` |
| 载荷 | `4,798` 个文件，`1,584,232,265` 字节 |
| HechaoEconomy | `0.1.1`，SHA-256 `1EFC1AE4BD1E935B1A4BC3A4D51069F94DDD7B3A39BDA10524FC78EC0F6C4DEA` |
| HechaoEconomyScreen | `0.1.0`，SHA-256 `B18958E3A30698D6AC2618662BC36D48A103D51EB2A5FBFD9874D08E6B241F8F` |

`Test-SkyrealmImportPackage.ps1` 已通过全部载荷哈希、禁止文件、配置所有权、受管启动
脚本、双端 JAR 一致性、Bukkit 命令/权限和 NeoForge 资源合同。后台同源 Inspector 返回：

- `Canonical`，`hasBlockingIssues=false`；
- Minecraft `1.21.1`、NeoForge `21.1.228`、Java `21`、最大玩家 `20`；
- 唯一服务端入口 `server/start.bat`；
- 客户端 `4,457` 个文件，SHA-256
  `4AA8470D29A78E8D0FEC3449BD17CA9DCA201BC32DF2D6BE075641A341AA9B12`；
- 服务端 `347` 个文件，SHA-256
  `A0B279062B142B7A951767FA510282B54F21F8D158504B109DB7EB351322F06C`。

代码回归为完整 `.NET 730/730`、API `311/311`、Agent `57/57`、Vitest `11/11`、
Playwright `26/26`；Release 构建 `0` 警告、`0` 错误。

## 生产门禁

1. 先确认货币正式使用“金币+两位小数”还是“赫币+整数”；第一笔真实账本数据后不能
   直接改变精度。
2. 在隔离 PostgreSQL 应用迁移 `029`，完成账本守恒、并发、幂等与恢复演练。
3. 备份 API、数据库、owl5 Agent 配置、`E:\Survival2` 与世界；验证回滚可读。
4. 检查现有 `survival2/start.bat` 含 `HECHAO_MANAGED_START`，计划任务引用同一绝对路径。
5. 先发布 API `0.31.0`，再仅重启 Agent 到 `0.6.0`；只读核对 `activity` 与 `survival2`
   两个部署目标，不控制 Minecraft。
6. 后台上传本包，明确选择 `survival2`，只进入客户端 `Test` 通道并在停服状态部署；
   部署结束保持停止。
7. 配置运行时经济令牌和 Velocity forwarding 后，按 `2/3/5/20` 人完成真实灰度。

任何门禁失败都停止前滚：API 使用原子 release 回滚，Agent 使用安装器备份回滚，
`survival2` 使用受控目录回滚；不删除账本、审计、导入记录或不可变 OSS 对象。

# 赫朝整合包制作指令

本文件适用于解压后的整个整合包导入模板。实际活动源码仓库和赫朝平台仓库中的
`AGENTS.md` 同时生效；当前用户要求和实时验证结果优先。

## 开始前

1. 先读 `00-从这里开始.md`、`02-标准上传包格式.md`、
   `03-客户端制作规范.md` 和 `04-服务端制作规范.md`。
2. 再读 `reference/platform-docs/` 中的整合包、活动企划、活动通道和新服务端规范。
3. 在实际活动源码仓库运行 `git status --short --branch`，读取其构建文件和项目指令。
4. Minecraft、加载器、Java、模组、服务端核心和地图版本必须从真实文件核验，不凭文件名
   或旧文档猜测。
5. 所有 PowerShell 业务脚本使用 PowerShell 7（`pwsh`）。

## 制作边界

- 最终上传包必须使用根级 `hechao-pack.json` 和 `client/`、`server/`、可选
  `shared/` 的规范布局。
- `shared/` 只放两端字节完全相同且确实都要加载的文件。无法证明时显式分到客户端或
  服务端。
- 原始 CurseForge/MRPACK 远程项目引用必须先解析并下载成完整文件，不能直接交付。
- 客户端必须包含 `hechao-profile.json`、完整版本 JSON/JAR、所需 assets、libraries、
  mods 和配置；Java 运行时由启动器管理，不打入档案。
- 服务端必须包含全部运行依赖、受管 `start.bat`、唯一 `-Xms/-Xmx` 参数文件和安全的
  `server.properties`。部署目标固定为 `activity / owl5 / 127.0.0.1:25568 /
  owl5-activity-slot`。
- 不把 Velocity Authorizer、Lobby Guard、LuckPerms Tier Agent 或服控代理复制进活动
  后端。原生 Fabric、Forge、NeoForge 不放 Bukkit 插件。
- 不在整合包里保存 `forwarding.secret` 或任何凭据。该文件由主机受控快照保留和注入。
- 不把世界、日志、构建目录、账号缓存、下载缓存或测试数据误当客户端资源。

## 完成标准

1. 运行 `tools/Test-HechaoPackageImportSource.ps1`，结果无错误。
2. 运行 `tools/New-HechaoPackageImportArchive.ps1`，生成 ZIP、SHA-256 和报告。
3. 在全新目录解压客户端并验证启动过程；在专用服务端验证无图形启动和安全停服。
4. 对客户端与服务端同名共同 JAR 比较 SHA-256。
5. 最终报告包含源码提交、两端文件数和字节数、运行时版本、共同 JAR 摘要、自动测试、
   真人测试、许可证、地图来源和回滚目标。
6. 未获得当前任务明确授权时，不连接生产、不上传后台、不部署、不启动服务器。

# 天域远征工业季经济集成 1.0.9

> 状态：赫朝后台一键导入候选已完成并通过三重离线验证；未上传后台、未部署
> API/Agent、未执行迁移、未注入生产令牌，也未启动或重启任何 Minecraft 服务端。
> 日期：2026-08-14

## 当前可用制品

| 项目 | 值 |
| --- | --- |
| ZIP | `E:\天域远征工业季-赫朝一键导入-1.0.9.zip` |
| 大小 | `1,585,225,497` 字节 |
| SHA-256 | `DF01417B20435CF9DD6C7E776E429057E693A5B297D9332B384F5203556A5DD3` |
| 载荷 | `4,796` 个文件，`1,584,237,449` 字节 |
| ZIP 校验旁车 | `E:\天域远征工业季-赫朝一键导入-1.0.9.zip.sha256` |
| 逐文件校验旁车 | `E:\天域远征工业季-赫朝一键导入-1.0.9.zip.payload.sha256` |
| HechaoEconomy | `0.1.2`，`366,851` 字节，SHA-256 `D538110ECC3320F5F7DE7AAEC9FF872D56BAD9C228D6D8482BAF88BAF595D56E` |
| HechaoEconomyScreen | `0.1.2`，`23,600` 字节，SHA-256 `B2E8913BD61EA3C8C36A41364E049A0699E51C59D170663661435688179A59C1` |

`1.0.5` 至 `1.0.8` 均为历史缺陷候选，不得上传或部署。它们虽然可能被旧 Inspector
判为 `Canonical`，但客户端目录、启动档案、Java 元数据、包根文件或生产端口仍不满足
完整导入链。`1.0.9` 是当前唯一允许进入后台候选导入流程的版本。

## 一键导入合同

- ZIP 根部只包含 `hechao-pack.json`、`client/` 和 `server/`；内部审计清单改为 ZIP
  外部旁车，不会被 Publisher 当成游戏文件发布。
- 客户端载荷直接位于 `client/` 下，不再保留 `client/.minecraft/`。Publisher 拆分后会把
  `mods/`、`versions/`、`libraries/` 和 `assets/` 安装到档案的单层 `.minecraft`。
- `client/hechao-profile.json` 固定启动版本 `天域远征工业季 1.21.1` 和 Java `21`；对应
  版本 JSON、版本 JAR 均存在，版本 JSON 的 `javaVersion.majorVersion` 也是 `21`。
- 服务端固定为 `server-ip=127.0.0.1`、`server-port=25565`、`online-mode=false`，与
  `survival2` 受控目标一致；`server/start.bat` 保留 `HECHAO_MANAGED_START` 门禁。
- 玩家缓存 `server/usercache.json`、已知 LuckPerms/SkyrealmCore 本地数据库、启动器程序、
  生产经济令牌和 `forwarding.secret` 均不进入包。
- `server/plugins/HechaoEconomy-0.1.2.jar`、服务端屏幕模组和客户端屏幕模组均在正确
  侧；双端屏幕 JAR 逐字节一致。

## 服主快捷商品设置

- 唯一管理权限为 `hechao.economy.admin`。
- 服主手持原版物品或普通无自定义数据的模组物品，可从自定义屏幕进入“服主回收设置”，
  也可执行 `/heco product`。
- 常用价格可直接点击；精确设置使用
  `/heco product set <单价> [个人日限] [全服日限]`。
- 暂停回收使用 `/heco product remove`。玩家用 `/shop` 查看当前启用目录。
- 命名、附魔、容器、带数据组件或其他元数据物品继续故障关闭。所有写入由服务端校验，
  并通过外置令牌进入 Economy Service 审计。

## 验证证据

1. `Test-SkyrealmImportPackage.ps1` 返回 `Valid`，逐项核对 `4,796` 个载荷、两个旁车、
   双端 JAR、启动元数据、服务器属性、Essentials/TAB 所有权和禁止文件。
2. 完整解压后运行通用 `Test-HechaoPackageImportSource.ps1 -ExpectedServerPort 25565`
   返回 `Valid`：客户端 `4,453`、服务端 `342`、共享 `0`、警告 `0`。
3. 后台同源 `Hechao.ModpackInspector` 返回 `Canonical`、`hasBlockingIssues=false`、问题
   `0`；拆分后的客户端没有 `.minecraft/` 前缀，并直接包含启动档案、版本文件和屏幕模组。
4. Inspector 客户端拆分包 SHA-256 为
   `AA965475CD42E74F3AA8BC8A4D8EDBB7BE368E2B8A1600D1C200C04CC21705B8`；服务端拆分包
   SHA-256 为 `51D4D1F46A7208FBA34EA590E562CE0A1B2A3DFCEA011B45F37E9EBDA37238F1`。
5. PowerShell 导入模板回归通过，包括空 `shared/` 文件集、秘密拒绝和同名 JAR 不一致
   拒绝；完整 `.NET` 解决方案 `731/731` 通过。
6. HechaoEconomy `10/10`、HechaoEconomyScreen `3/3` 的 Java 测试与可复现 JAR 构建已通过。

## 后台导入与生产边界

后台上传时必须明确选择 `survival2`，并输入包含目标 ID 的确认文本。客户端先发布到
`Test` 通道，服务端部署后保持停止。生产前还必须完成：

1. 确认货币正式名称和精度；第一笔真实账本数据前不得再变更。
2. 在隔离 PostgreSQL 应用迁移 `029`，完成账本守恒、幂等、并发和恢复演练。
3. 备份 API、数据库、owl5 Agent 配置、`E:\Survival2` 与三个世界目录。
4. 在旧受控目录预置外部 `plugins\HechaoEconomy\economy-token.txt` 和
   `forwarding.secret`；发布 API `0.31.1` 与 Agent `0.6.0` 后核对两者未被覆盖。
5. 使用真实客户端完成登录、下载、启动、普通模组物品设置、拒绝型物品、交易补偿、
   Velocity、LuckPerms、语音和世界恢复验收。
6. 最后按 `2/3/5/20` 人完成 TPS、MSPT、GC、内存和交易并发灰度。

任一门禁失败都停止前滚并使用既有 API release、Agent 安装器和受控目录备份回滚；不得
删除账本、审计、导入记录或不可变 OSS 对象。

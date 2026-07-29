# 代理单层协议转换生产运维

> **历史流程，禁止执行新的生产迁移。** 2026-07-29 已确认大厅只保留为内部前置能力
> 承载器，玩家只通过赫朝启动器切服。ViaVersion/ViaBackwards 大厅回程和 `lobby`
> 玩家授权开关不再使用；现行隔离与回滚要求见
> [`LAUNCHER_ONLY_SERVER_SWITCHING.md`](LAUNCHER_ONLY_SERVER_SWITCHING.md)。
> 本文仅用于解释既有迁移与回滚证据。

## 1. 边界

本流程只处理 owl5 的以下两个实例：

- Velocity：`E:\Velocity`，任务 `Codex-Velocity-Live`，端口 `25577`
- Lobby：`E:\LobbyServer`，任务 `Hechao-Server-Lobby`，端口 `25566`

Survival1、Survival2、Activity、DollNight、owl9 恐怖整蛊和 owl9 真正 PVP 均不在
迁移写入范围。脚本不会修改世界、modern forwarding 密钥、Velocity 配置内容或
Authorizer 凭据。

目标架构：

- Velocity `4.0.0` build `6` 使用独立 Temurin Java `25.0.4+7`
- Velocity 加载 ViaVersion/ViaBackwards `5.11.0`
- Lobby 禁用 ViaVersion/ViaBackwards
- Authorizer `0.3.1` 保持 `monitor`
- API 目录默认全部禁止协议转换，生产验收时只为 `lobby` 开启

## 2. 控制器

[`Manage-ProxyProtocolTranslation.ps1`](../deploy/windows/velocity-production/Manage-ProxyProtocolTranslation.ps1)
默认只读：

```powershell
.\Manage-ProxyProtocolTranslation.ps1 -Action Status
```

只有旧核心、旧 Authorizer、代理禁用 Via、大厅启用 Via、任务动作、制品哈希、两个
监听和零客户端连接全部匹配时，`safeForMigration` 才为 `true`。

生产迁移必须显式确认：

```powershell
.\Manage-ProxyProtocolTranslation.ps1 -Action Migrate -ConfirmMigration
```

执行顺序：

1. 创建受限 ACL 的时间戳备份，并逐文件复核 SHA-256。
2. 再次确认入口代理没有连接，然后先停止 Velocity，封住新连接。
3. 向 Lobby 发送 `save-all flush`，从新日志确认保存完成，再发送 `stop`。
4. 替换 Velocity 核心和 Authorizer，移动 Via 文件扩展名，并只修改 Velocity 任务的
   Java 路径。
5. 先启动 Lobby，再启动 Velocity；核对监听、版本、插件归属和新日志。
6. 任一步失败时自动使用本次备份恢复旧核心、旧插件、旧任务 XML 和旧 Via 归属。

迁移成功后，备份目录包含 `prechange.json`、`migration.json`、任务 XML、变更前日志
和全部相关文件。输出不含玩家标识、令牌或 forwarding 密钥。

## 3. 显式回滚

先关闭 Lobby 的目录开关，再使用成功迁移输出中的备份目录：

```powershell
.\Manage-ProxyProtocolTranslation.ps1 `
  -Action Rollback `
  -BackupDirectory 'E:\manual-backups\velocity-proxy-protocol-translation-<UTC>' `
  -ConfirmRollback
```

回滚同样要求代理零连接，并优雅保存 Lobby。恢复结果为：

- Velocity 旧核心与原 Java 21 任务动作
- Authorizer `0.3.0`
- 代理 Via 禁用
- Lobby Via 启用

## 4. API 开关

API `0.21.0` 与迁移 018 必须先上线。根运维脚本只允许更改 `lobby`，要求传入预期旧值，
并在同一事务中写审计记录：

```bash
sudo ./set-lobby-protocol-translation.sh true false --confirm-production
sudo ./set-lobby-protocol-translation.sh false true --confirm-production
```

启用前脚本确认其他目标没有打开协议转换。发生生产回程问题时，先执行第二条命令恢复
版本硬拒绝，再决定是否回滚代理。

## 5. 验收

生产切换后至少完成：

1. 1.21.11 基础客户端进入 Lobby、Survival1 和 Survival2。
2. Activity 与 DollNight 使用各自档案进入。
3. 恐怖整蛊 1.20.1 客户端进入历史目标 `pvp`，执行 `/hub` 返回 Lobby。
4. 再次进入恐怖整蛊、再次 `/hub`、断线重连和正常退出。
5. 核对代理、Lobby、恐怖整蛊和客户端协议/解码错误均为 `0`。
6. 确认 Authorizer 仍为 `monitor`，真正 PVP `E:\MinecraftServer` 未启动、未修改。

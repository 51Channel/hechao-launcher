# 赫朝启动器 API 0.14.1 发布记录

> 生产发布 ID：`0.14.1-20260726T190856Z`
> 源码提交：功能 `ae998af`，启动热修复 `da12c41`
> 直接回滚目标：`0.13.0-20260726T173536Z`
> 数据库迁移：`10`

## 功能

- 服务器目录增加维护公告、一次性开放时间和关闭时间。
- 玩家目录与 Velocity 授权使用同一排期解析；手动 `Maintenance` / `Closed`
  始终优先，排期不会启动或停止 Minecraft 进程。
- 管理后台增加玩家搜索和每个服务器的最终访问结果预览。
- 管理后台增加带原因、到期时间和乐观并发修订号的单服允许/拒绝规则。
- 有效拒绝规则优先于允许规则；允许规则不能绕过账号禁用、未绑定正版、
  服务器归档或服务器关闭。
- 启动器目录优先显示管理员配置的服务器公告。

## 迁移与兼容

迁移 10 为 `launcher.servers` 增加 `announcement`、`opens_at`、`closes_at`，
为 `launcher.server_access_overrides` 增加 `revision`、`updated_at` 和查询索引。
迁移只做扩展，`0.13.0` 回滚后会忽略新字段，不需要删除列或表。

## 启动故障与热修复

首次候选 `0.14.0-20260726T185852Z` 已完成迁移 10，但 ASP.NET 在构建
`DELETE /v1/admin/users/{userId}/access-rules/{serverId}` 时拒绝自动推断请求体。
安装脚本未通过 `/readyz`，自动把 `current` 恢复到 `0.13.0`，公网业务保持可用。

`0.14.1` 为删除请求显式添加 `[FromBody]`。部署前使用生产环境文件和同一数据库，
在 `127.0.0.1:18090` 启动完整程序，确认 `/healthz`、`/readyz` 为 `200`，
且启动日志没有路由构建异常。预检进程和目录随后已清理。

## 发布物

- 单文件：`103,854,131` 字节
- 单文件 SHA-256：
  `F02CC7AAC3AE4FC8726548E3777D231D035B03E19487CAB32627333CEBBB8A3A`
- 归档：`45,365,349` 字节
- 归档 SHA-256：
  `877C9EBE6CDB5B611E0495F69BC759D6D51B4A1A1AF8D60A906F7BEC57E8959E`

## 备份

- 数据库：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260726T191147Z.dump`
- 数据库 SHA-256：
  `c2d9563544bffdf4060bc51ff93a5c27d1d13c84c1d25f6ec3c963aaa7181029`
- API、环境、systemd 与 Nginx：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.14.1-20260726T191147Z.tar.gz`
- 完整备份 SHA-256：
  `e2420923ac01f2bae6e7a81c3eda56370fb7c50e8afa8e4ab4eabcc1e6f669b7`

数据库校验和、API 归档校验和、`pg_restore --list` 和备份内容检查均通过。

## 验证

- Debug 与 Release .NET 测试：`251/251`
- Velocity Java 测试：`11/11`
- 管理前端 JavaScript 语法检查：通过
- 本机与公网 `/healthz`、`/readyz`：`200`
- 公网玩家目录：`200`
- 无效 Bearer：`401`
- 未登录管理玩家接口：`401`
- API 域名 `/admin/`：`404`
- 管理域名登录跳转后：`200`
- `hechao.world` 与 `api.hechao.world`：`200`
- 迁移记录：`1` 至 `10`
- 新服务启动后的 warning 及以上日志：`0`

本次只重启 `hechao-launcher-api.service`。没有启动、停止或重启 Minecraft、
Velocity、大厅、生存服或活动服。

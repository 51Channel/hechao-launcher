# API 0.24.2 正式发布

- 发布 ID：`0.24.2-20260731T141731Z`
- 源码提交：`bc04fea8d525663b3ae24f4a6dcfc6d1b219c986`
- 正式标签：`api-v0.24.2`
- 应用切换时间：2026-07-31 22:24 CST
- 启动器通道生效时间：2026-07-31 22:26 CST

## 变更

管理后台新增运行中服务器发现。打开新增服务器抽屉时，会从现有服控概览中列出代理在线、物理服运行中且尚未进入完整目录的目标，并辅助填写 ID、名称、短名、图标、状态、人数、Velocity 目标和监控设置。可识别的运行时版本与加载器会一并填充；管理员仍需确认客户端档案并主动保存。

发现功能是只读辅助，不会自动创建目录记录，不会启动、停止或重启服务器。owl9 的命名边界保持不变：`pvp` 是恐怖整蛊，`pvp-purpur` 才是真正 PVP。

## 制品

| 制品 | 字节 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.24.2-20260731T141731Z.tar.gz` | `45,650,671` | `89BDAC08E0E129A6E0F2F820F3357EDDB7A777538146173C5065238936C88EAC` |
| `Hechao.Api` | `104,608,311` | `F7C1529969216D1287F40DE2000D2BAA3A08FD87B02E389C0292DF5B749A6D26` |

归档包含 `113` 个条目和 `108` 个文件；路径穿越检查、独立解压和二进制哈希复验通过。归档不包含 PDB、环境文件或凭据。

## 部署与验收

- 完整解决方案测试 `492/492`，其中 API `228/228`；管理后台 JavaScript 语法检查通过。
- 部署前生产版本为 `0.24.1-20260731T105946Z`，服务 `active`、`NRestarts=0`，本机健康与就绪正常。
- 发布前数据库备份：`/var/backups/hechao-launcher/database/hechao-launcher-20260731T142250Z.dump`，`1,528,097` 字节，SHA-256 `C427C8F606D754C2EE2AAEC27BFBBA4399930957E1B1B010DDD34740A54A74E2`；`pg_restore --list` 通过。
- 本机与远端归档、安装脚本和 systemd 单元 SHA-256 一致后才执行原子切换。
- `current` 指向 `/opt/hechao-launcher-api/releases/0.24.2-20260731T141731Z`。
- `hechao-launcher-api.service` 为 `active/running`，`NRestarts=0`。
- 本机与公网 `/healthz`、`/readyz` 均返回 `200`，版本为 `0.24.2`，数据库为 `ready`；公网目录返回 `200`。
- 发布后错误级 journal 为 `0`；生产 `admin.js` SHA-256 与仓库一致。

应用发布和更新通道切换共正常重启 API 两次。没有执行任何 Minecraft 服控命令，也没有启动、停止或重启 owl5/owl9 上的游戏服。

## 回滚

应用级直接回滚目标为：

`/opt/hechao-launcher-api/releases/0.24.1-20260731T105946Z`

[`install-release.sh`](../deploy/linux/install-release.sh) 在新版本 `/readyz` 失败时会自动恢复旧符号链接。启动器通道配置另有部署前环境备份；回滚通道只需恢复该备份并重启 `hechao-launcher-api.service`。

结构化证据见 [`evidence/API_0.24.2_PRODUCTION_DEPLOYMENT_2026-07-31.json`](evidence/API_0.24.2_PRODUCTION_DEPLOYMENT_2026-07-31.json)。

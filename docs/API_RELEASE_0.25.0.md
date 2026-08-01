# API 0.25.0 正式发布

正式发布 ID：`0.25.0-20260801T105011Z`

源码提交：`950bfeb3d4aabe2ee95225bbace53ec023fa4828`

正式标签：`api-v0.25.0`

生产切换时间：2026-08-01 18:57 CST

## 功能范围

管理后台现在支持可信管理员设备。管理员仍须由赫朝启动器创建 90 秒一次性票据；完成一次 MFA 后，可以把当前浏览器配置文件标记为可信设备。后续由同一管理员、同一浏览器从启动器打开后台时，可信设备只负责把新后台会话标记为已完成 MFA，不能单独创建管理员身份。

可信令牌为 256 位随机值，浏览器 Cookie 使用 `__Host-HechaoAdminTrusted`，带 `HttpOnly`、`Secure`、`SameSite=Strict` 和 `Path=/`。PostgreSQL 只保存 SHA-256。默认有效期 30 天，每个管理员最多保留 3 个可信设备。显式退出后台、管理员停用、改密或撤销全部会话会撤销相应信任。

## 发布物

| 制品 | 字节 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.25.0-20260801T105011Z.tar.gz` | `45,661,020` | `C45DF2C7BF5C9D515864434E24B17FB64B8910F4F4B4BFF65A3B11F19A2EB422` |
| `Hechao.Api` | `104,640,051` | `B69D32F1A374BE2BF96E875931861B7F09F786348B7C7AD7A1F26883559E2E9A` |

归档包含 113 个条目和 108 个文件。危险路径、链接、PDB、环境文件和凭据文件均为 0；独立解压后的主程序哈希与构建目录一致。

## 验证

- 完整解决方案：`570/570`。
- API：`239/239`。
- 管理后台 JavaScript 语法检查：通过。
- 发布前数据库备份：`/var/backups/hechao-launcher/database/hechao-launcher-20260801T105549Z.dump`，`1,830,599` 字节，SHA-256 `E079173906E3846F0B3585CEF694EDBF428979BF90F620227FB6111D581AC15C`。
- 数据库备份通过同名 SHA-256 校验和 `pg_restore --list`，目录项为 `185`。
- 原子切换后当前目录为 `/opt/hechao-launcher-api/releases/0.25.0-20260801T105011Z`。
- 数据库迁移为 `21/21`，`launcher.admin_trusted_devices` 已创建。
- `hechao-launcher-api.service` 为 `active`，PID `917486`，`NRestarts=0`，错误日志为 `0`。
- 本机与公网 `/healthz`、`/readyz` 均为 `200`，版本为 `0.25.0`，数据库为 `ready`。
- `https://admin.hechao.world/admin/` 为 `200`；错误主机 `https://launcher-api.hechao.world/admin/` 仍为 `404`。

## 真实浏览器验收

2026-08-01 19:05 CST，管理员通过启动器票据完成最后一次 MFA，并勾选“信任这台电脑”。生产库产生 1 个有效可信设备，过期时间为 2026-08-31 19:05 CST，并写入 `admin.trusted_device.created` 审计。

2026-08-01 19:09 CST，再次从赫朝启动器打开管理后台时直接进入控制台，没有显示 MFA 页面；生产审计新增 `admin.trusted_device.used`。验收没有读取、输出或保存 Cookie、可信令牌、MFA 动态码或启动器票据。

不要使用“退出管理会话”验证此功能，因为显式退出会按设计撤销当前可信设备。其他电脑、无痕窗口和不同浏览器配置文件仍须完成 MFA。

## 回滚

直接回滚目标为 `/opt/hechao-launcher-api/releases/0.24.2-20260731T141731Z`。发布前 systemd 单元备份位于 `/var/backups/hechao-launcher/api-predeploy/pre-api-0.25.0-20260801T105011Z`。

[`install-release.sh`](../deploy/linux/install-release.sh) 会在新版本 `/readyz` 失败时恢复旧符号链接。回滚 API 时保留迁移 21 和 `launcher.admin_trusted_devices` 表，不删除 MFA、会话或可信设备数据；旧 API 不读取该表。回滚不要求也不允许重启任何 Minecraft 游戏服。

结构化证据见 [`evidence/API_0.25.0_PRODUCTION_DEPLOYMENT_2026-08-01.json`](evidence/API_0.25.0_PRODUCTION_DEPLOYMENT_2026-08-01.json)。

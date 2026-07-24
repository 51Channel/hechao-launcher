# 版本发布与 Git 工作流

> 生效日期：2026-07-23
> 原则：每个功能、修复和运维改版都必须进入 Git；生产状态与源码状态必须能够互相追溯

## 1. 提交边界

每项工作至少形成一个范围清楚的提交。常用前缀：

| 前缀 | 用途 |
| --- | --- |
| `feat:` | 新功能 |
| `fix:` | 缺陷修复 |
| `ops:` | 部署、监控、备份和服务器配置 |
| `docs:` | 只修改文档 |
| `test:` | 只补测试 |
| `chore:` | 构建、依赖和仓库维护 |

同一功能的源码、测试、无秘密部署脚本和运维文档应在同一个功能提交中，或使用相邻、可独立回滚的提交。不要把无关重构、游戏服内容、生成目录和秘密混入提交。

## 2. 上线定义

一次改版只有同时满足以下条件才算完成：

1. 源码和自动化测试通过。
2. 生成发布物并核对产品版本、文件结构和 SHA-256。
3. 部署前备份完成，部署后健康检查和旧业务回归通过。
4. 文档记录发布 ID、哈希、备份、当前模式、尚未执行的重启和回滚方法。
5. `git diff --check` 通过，暂存区不含秘密、构建产物或本地运行数据。
6. 创建明确提交并推送到用户指定远端。
7. 对正式发布创建与组件对应的注释标签。

未获得 Git 远端、提交身份或推送权限时，可以完成开发和部署准备，但必须把“尚未推送”列为明确阻塞，不能声称版本已归档。

## 3. 版本与标签

组件独立版本：

```text
launcher-v0.9.1
api-v0.6.0
velocity-authorizer-v0.1.0
publisher-v0.5.0
status-collector-v0.1.0
profile-base-1.21.11-v1.0.5
```

标签只指向已经测试、记录并推送的提交。测试构建不占用正式版本号；已上传的对象、清单、发布归档和标签不得覆盖，应发布更高版本。

Windows 启动器正式候选统一使用：

```powershell
.\tools\Build-WindowsInstaller.ps1
```

该入口必须产出 `Hechao-Launcher-Setup-<version>-win-x64.exe` 和同名 `.sha256`。发布记录同时登记安装包和安装后 `Hechao.Launcher.exe` 的版本、大小、SHA-256、代码签名状态，以及一次隔离目录安装/卸载结果。游戏数据目录不进入安装包或 Git；卸载测试必须确认其不在删除边界内。

## 4. 提交前检查

```powershell
git status --short
git diff --check
git diff --cached --check
git diff --cached --stat
git remote -v
git config --get user.name
git config --get user.email
```

至少复核以下内容没有进入暂存区：

- Microsoft、Minecraft、赫朝会话和内部同步令牌。
- OSS AccessKey、数据库口令、SSH 私钥和证书私钥。
- `artifacts/`、`bin/`、`obj/`、Gradle `build/`、日志、数据库和崩溃转储。
- VPS 密码、RDP 密码或只能保存在本机安全存储中的路径内容。

`.gitignore` 是最后一道误操作保护，不是秘密管理方案。已经误提交的秘密必须立即吊销和轮换，不能只从后续提交删除。

## 5. 发布提交模板

```text
feat: add Velocity launch authorization

- issue one-time launch grants immediately before process start
- enforce UUID, server state, tier and per-server overrides in API
- stage Velocity plugin in monitor mode without restarting the proxy
- document activation, verification and rollback

Tests: dotnet 140/140; Velocity 7/7
Production: API 0.5.0 healthy; plugin staged in monitor mode
```

提交正文只记录非秘密事实。远端推送后再把提交 ID 和标签补入发布记录；若因此修改文档，应形成一个小型 `docs:` 或 `ops:` 提交。

## 6. 当前初始提交

本目录在开始执行该规则时已经包含完整的平台源码，但 Git 尚无历史。第一次提交应作为“平台初始基线”，覆盖截至 API `0.5.0`、启动器 `0.6.0` 和 Velocity 插件 `0.1.0` 的现状。此后每项功能或改版必须独立提交并推送，不能继续累积为第二个巨型提交。

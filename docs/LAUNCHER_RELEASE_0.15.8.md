# 赫朝启动器 0.15.8 发布记录

- 发布日期：2026-08-15
- 功能源码提交：`a5696c5ee9b2b8934c024579f9c411d5351a6a2f`
- 正式构建源码提交：`30c438bb4e6e7ea6ba8f5c2b8afc9dff2d8cf85f`
- 正式标签：`launcher-v0.15.8`
- 配套 API：`0.30.7-20260814T144949Z`
- 生产通道切换时间：`2026-08-15T01:13:32Z`

## 变更内容

1. 所有已登录玩家都能看到可见活动并提前下载、更新、修复或删除对应客户端，不再因
   最低称号不足而同时失去活动和下载入口。
2. 活动可见性、客户端下载和进服权限正式拆分；`canJoin=false` 时活动页和服务器主页
   显示“称号权限不足”并禁用进服，执行命令前还会再次进行本地防御性拦截。
3. 最低称号与单服 `Allow` / `Deny` 共同决定进服结果，`Deny` 优先；永久服继续隐藏
   无权记录，Velocity 保留最终服务端门禁。
4. API `0.30.7` 已先行上线并完成生产临时身份探针；启动器 `0.15.8` 只消费该合同，
   没有修改游戏服、Velocity、活动排期或客户端档案内容。

## 构建与测试

| 制品 | 字节 | SHA-256 | 版本 | 签名 |
| --- | ---: | --- | --- | --- |
| `Hechao-Launcher-Setup-0.15.8-win-x64.exe` | `61,974,181` | `737DC71A63D7B3B1F0E4E7967162183C1BDED1C232E771855ED28DA626FA3BA1` | `0.15.8` | `NotSigned` |
| `Hechao.Launcher.exe` | `69,029,855` | `276A10753EAE317AE809B4A264A45B8974C9344D1A2657FA68277669CAFE2B53` | `0.15.8+30c438bb4e6e7ea6ba8f5c2b8afc9dff2d8cf85f` | `NotSigned` |

- 固定提交使用 `.NET SDK 10.0.302` 完成 Release 构建，0 warning、0 error。
- 完整解决方案 `748/748`、Launcher `229/229`、API `326/326`、Publisher `55/55`、
  ServerControlAgent `58/58`、Distribution `45/45`、StatusCollector `16/16`、Backup
  `12/12`、Modpack `7/7` 全部通过。
- `0.15.7 -> 0.15.8` 隔离覆盖安装、全新安装和两轮卸载均通过；设置、DPAPI 会话文件
  和验收开始时的既有启动器进程均保留。

## 私有 OSS 与更新通道

- 不可变对象：
  `releases/launcher/0.15.8/Hechao-Launcher-Setup-0.15.8-win-x64.exe`。
- 首次发布成功；第二次发布核对长度、版本、文件名与 SHA-256 后跳过，没有覆盖对象。
- 两轮独立签名下载均为 `200`，长度和 SHA-256 一致；匿名读取为 `403`，签名 URL 未
  进入终端、Git、文档或结构化证据。
- 发布前阿里云 OSS 曾对公开下载和发布凭据返回 `UserDisable`；写入前检查安全失败，
  没有创建或覆盖对象。主账号重新登录后，未修改控制台配置，旧版公开下载恢复为
  `200`，随后才继续正式上传和完整验收；不能把恢复与登录建立未经证实的因果关系。
- 生产更新通道已切换到 `LatestVersion=0.15.8`，最低支持版本保持 `0.12.3`。认证
  Launcher 会话确认 `0.15.7` 生成更新计划、`0.15.8` 不重复更新，并通过 API 签发
  地址完整下载 `61,974,181` 字节且 SHA-256 一致。
- 公开元数据、公开完整下载、官网 `/download`、API 健康与就绪、官网、管理站和中转站
  均通过；官网只显示 `0.15.8`。公网 `8090` 仍不可达，API 只监听 `127.0.0.1:8090`。

## 生产切换与回滚

- 切换前环境备份：
  `/etc/hechao-launcher-api/environment.launcher-updates.20260815T011332Z.bak`，
  SHA-256 `88B2058563E3957EF9275AB9F54F08E735BC4A676F146F0F1377E12F3546B483`。
- 更新后环境 SHA-256 为
  `F51EE4B814A545011B500DB5676E70E81B811A5B9FA3A340CA48871DABE6CC57`；配置和备份均为
  `root:root 600`。
- 只重启 `hechao-launcher-api.service`，API PID 从 `318675` 变为 `633784`，
  `NRestarts=0`；Publisher PID `2064` 与 Nginx PID `1742715` 未变化，发布后
  warning/error 为 `0`。
- 没有修改 PostgreSQL、企划、整合包、活动槽或服务器目录，也没有启动、停止或重启
  Minecraft、Velocity、Publisher、Nginx 或服控代理。
- 如分发异常，恢复上述环境备份并只重启 Launcher API，或禁用更新通道。已经安装
  `0.15.8` 的客户端不会自动降级；代码修复必须发布更高版本，不能覆盖本对象或标签。

## 剩余外部验收

活动尚未进入开放窗口，因此最低称号不足玩家在 Velocity 最终门禁被拒绝的真人路径仍
保留到开放窗口验收。API 临时身份探针和 Launcher 防御性门禁均已通过，但不能替代该
真实网络路径；平台仍按 `2/3/5/20` 人逐级灰度。

结构化证据见
[`evidence/LAUNCHER_0.15.8_RELEASE_ACCEPTANCE_2026-08-15.json`](evidence/LAUNCHER_0.15.8_RELEASE_ACCEPTANCE_2026-08-15.json)。

# Publisher 1.3.0 候选发布

- 候选日期：2026-08-08
- 上一正式版本：`1.2.1`
- 目标标签：`publisher-v1.3.0`

## 范围

`upload-launcher-release` 现在支持两种互斥的 OSS 凭据来源：

- Windows 管理机继续使用 `--credential-dpapi` 和 `--dpapi-entropy-label`；
- Linux 一次性发布任务使用 `--credential-systemd` 指向 systemd 运行时凭据文件。

两种模式最终进入同一个 `OssCredentialStore` 和 `LauncherReleaseUploader`。对象键、长度、SHA-256、`release-version`、`original-filename`、私有 ACL、禁止覆盖和短时签名链接规则没有变化。

## 部署边界

- 本次只把 Linux 单文件候选放入独立暂存目录，通过一次性受限 `systemd-run` 发布启动器安装包。
- 不替换 `/opt/hechao-package-publisher/current`，不重启或停止 `hechao-package-publisher.service`。
- 不操作 Launcher API、Minecraft、Velocity、服控代理或游戏服务端。
- systemd 加密凭据只在一次性任务的 `$CREDENTIALS_DIRECTORY` 中解密；明文不进入命令参数、日志、Git 或发布证据。

## 候选门槛

- [x] Publisher 单元测试 `55/55`、完整解决方案 `704/704` 通过，零编译警告。
- [ ] Linux `linux-x64` 自包含单文件构建并核对版本、长度和 SHA-256。
- [ ] 使用测试 systemd 凭据完成离线参数与权限预检。
- [ ] 启动器 `0.15.0` 首次上传、第二次校验跳过、签名回读和匿名拒绝通过。

## 回滚

一次性发布任务失败时不切换启动器更新通道；删除独立暂存目录即可。常驻 Publisher Agent 仍保持 `1.2.1`，无需服务回滚。已经成功写入的 OSS 对象不可删除或覆盖，但在 API 更新通道切换前不会被玩家发现。

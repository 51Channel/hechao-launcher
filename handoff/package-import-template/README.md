# 赫朝整合包导入模板

这是一份给活动制作人员及其 Codex 使用的离线交接模板。它定义赫朝后台能够稳定识别的
客户端与服务端整合包格式，并提供本地校验和打包工具。

本模板按 Launcher API `0.30.1`、整合包描述协议 `schemaVersion=1` 和当前唯一活动槽
约束整理。后续平台升级时，应以目标仓库实时源码和运维文档为准。

## 两种 ZIP 不要混淆

- `Hechao-Package-Import-Template-*.zip`：本交接包，给开发者或 Codex 阅读，不能上传后台。
- `Hechao-<profile-id>-<version>.zip`：使用包内工具从完整客户端和服务端源生成，才是后台
  “整合包导入”页面接收的业务 ZIP。

## 最短使用路径

1. 阅读 `AGENTS.md` 和 `00-从这里开始.md`。
2. 把 `01-给Codex的首条消息.md` 中的任务模板交给负责制作整合包的 Codex。
3. 按 `02-标准上传包格式.md` 准备独立工作目录。
4. 分别按 `03-客户端制作规范.md` 和 `04-服务端制作规范.md` 完成两端载荷。
5. 运行：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\Test-HechaoPackageImportSource.ps1 `
  -SourceDirectory <完整源目录>

pwsh -NoLogo -NoProfile -File .\tools\New-HechaoPackageImportArchive.ps1 `
  -SourceDirectory <完整源目录> `
  -OutputArchive <输出ZIP>
```

6. 一并交付业务 ZIP、同名 `.sha256` 和 `.report.json`，再按
   `05-导入与企划流程.md` 由管理员上传。

## 固定结论

- 推荐且唯一受本模板保证的上传格式是规范化 ZIP，不是原始 CurseForge 导出或仅含远程
  引用的 MRPACK。
- 上传 ZIP 根目录直接包含 `hechao-pack.json`、`client/`、`server/`，可选
  `shared/`；不要再套一层目录。
- 客户端必须是可独立安装和启动的完整 `.minecraft` 内容，但 `client/` 下不要再嵌套
  `.minecraft/`。
- 服务端必须是可在独立目录启动的完整服务端，且根级包含 `server.properties`、
  `eula.txt`、`user_jvm_args.txt` 和符合受管契约的 `start.bat`。
- `forwarding.secret`、账号、令牌、私钥、Cookie、日志和玩家缓存永远不进入整合包。
- 新企划先“仅发布并入库”，完成测试并把客户端精确清单推进 `Production` 后，再在日历
  绑定和部署。部署完成仍保持停服。

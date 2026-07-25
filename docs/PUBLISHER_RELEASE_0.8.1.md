# Hechao Publisher 0.8.1 发布记录

> 发布日期：`2026-07-25`
>
> 标签：`publisher-v0.8.1`
>
> 源码提交：`204391552e6f159451d1fc81c59f1ef1bca60501`

## 版本目标

本版本增加启动器安装包的私有 OSS 发布流程，并修复 Alibaba Cloud OSS V2 SDK 将服务端错误包装在外层操作异常时的识别问题。

启动器发布固定使用 `releases/launcher/<version>/Hechao-Launcher-Setup-<version>-win-x64.exe`。上传前重算本地 SHA-256；远端同名对象只有在长度、SHA-256、版本和原文件名元数据全部一致时才允许跳过。新对象显式设置私有 ACL、Content-MD5、禁止覆盖请求头和下载文件名，上传后再次回读元数据。通过校验后只生成 5 至 1440 分钟的 V4 签名下载链接。

发布 RAM 策略 `HechaoLauncherOssObjectPublish` 已升级为 v3，只允许以下资源：

- `acs:oss:*:*:hechaoworld/objects/*`
- `acs:oss:*:*:hechaoworld/releases/launcher/*`

允许动作仍只有 `oss:GetObject` 与 `oss:PutObject`，没有 Bucket 列举、其他前缀读取、删除对象或版本管理权限。

## 发布制品

| 项目 | 值 |
| --- | --- |
| EXE | `Hechao.Publisher.exe` |
| EXE 大小 | `74,046,754` 字节 |
| EXE SHA-256 | `2318900FCD04EA52AF7ADD79DF29EEDD97EAE7E1E81ECFC0E4BB20D19E6F1383` |
| FileVersion | `0.8.1.0` |
| ProductVersion | `0.8.1+204391552e6f159451d1fc81c59f1ef1bca60501` |
| ProductName | `Hechao.Publisher` |
| Authenticode | `NotSigned` |
| ZIP | `artifacts/releases/Hechao-Publisher-0.8.1-win-x64.zip` |
| ZIP 大小 | `32,089,367` 字节 |
| ZIP SHA-256 | `DDE14CB14DAE8BC4810A39E1636369F158D97844D42B0E865023B59D13226C32` |

ZIP 只包含一个 `Hechao.Publisher.exe`，压缩包内 EXE 的大小与 SHA-256 均和构建原件一致。

## 验收结果

- 完整解决方案测试：`168/168` 通过。
- 发布器测试：`29/29` 通过。
- 格式检查：通过。
- Git 差异空白检查和暂存凭据检查：通过。
- RAM v3 保存后回读源码，资源与动作和仓库模板一致。
- 启动器 `0.10.0` 首次上传成功：`61,796,065` 字节。
- 远端对象键：`releases/launcher/0.10.0/Hechao-Launcher-Setup-0.10.0-win-x64.exe`。
- 远端 SHA-256：`E2E14306882EF072016F35D740D2F06A7C8D12F63FFE28DD0F6A2C07B24D4876`。
- 匿名永久直链返回 `403`。
- 24 小时签名链接完整下载后的大小、SHA-256 和 `NotSigned` 状态均与本地候选一致。
- 第二次执行报告既有对象已验证，没有上传或覆盖。
- 临时下载验证文件已清理，完整签名链接没有进入 Git 或文档。

`publisher-v0.8.0` 在生产预检中发现 SDK 包装 404 的兼容问题。该次执行只发出 `HeadObject`，没有执行 `PutObject` 或创建远端对象；标签保留用于追溯，不移动也不覆盖，由 `0.8.1` 正式取代。

验收过程没有启动、停止或重启 Minecraft、Velocity、大厅、生存服或活动服。

## 回滚

启动器安装包保持私有且不可覆盖。若暂停内部灰度，只需停止生成和发送新签名链接，并等待现有链接到期；不要把对象改为公共读，也不要删除或覆盖同版本对象。若撤回发布权限，可把 RAM 策略恢复为只含 `objects/*` 的历史版本，但已经签发的链接仍可能在原到期时间前有效。

发布器回退到 `0.7.0` 不会影响已有对象，但它不支持启动器安装包路径。`0.8.0` 存在已知 404 包装兼容问题，不应继续用于生产发布。

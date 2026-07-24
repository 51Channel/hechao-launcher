# Hechao Publisher 0.7.0 发布记录

> 发布日期：`2026-07-24`
>
> 标签：`publisher-v0.7.0`
>
> 源码提交：`ac7bc8045c4c5f0b10b84987b8a8cb6f02bb3fca`

## 版本目标

本版本修复版本控制 OSS Bucket 中重复上传内容寻址对象的问题。发布器在写入前读取远端对象元数据，确认对象已存在且内容一致时直接跳过；任何长度或 SHA-256 元数据不一致都会终止发布，不会覆盖远端对象。

发布 RAM 策略 `HechaoLauncherOssObjectPublish` 当前为 v2，只允许在 `acs:oss:*:*:hechaoworld/objects/*` 上执行：

- `oss:GetObject`：仅供 `HeadObject` 读取对象元数据。
- `oss:PutObject`：仅供写入缺失的内容寻址对象。

该身份没有 Bucket 列举、其他前缀读取、删除对象或版本管理权限。

## 发布制品

| 项目 | 值 |
| --- | --- |
| EXE | `Hechao.Publisher.exe` |
| EXE 大小 | `74,022,178` 字节 |
| EXE SHA-256 | `78C190972D00C40A1066A6ACB21BE1624E2AF7D08F2FB128D9768E662FEC7BAC` |
| FileVersion | `0.7.0.0` |
| ProductVersion | `0.7.0+ac7bc8045c4c5f0b10b84987b8a8cb6f02bb3fca` |
| ProductName | `Hechao.Publisher` |
| Authenticode | `NotSigned` |
| ZIP | `artifacts/releases/Hechao-Publisher-0.7.0-win-x64.zip` |
| ZIP 大小 | `32,090,108` 字节 |
| ZIP SHA-256 | `E05B589976D033015D1FC05D276FE4E19694B9BD7A359569A1AE0473AF1F2F18` |

ZIP 只包含一个 `Hechao.Publisher.exe`。从 ZIP 读取到的文件大小和解压后的 SHA-256 均与构建原件一致，解压程序报告版本 `Hechao.Publisher 0.7.0`。

## 验收结果

- 完整解决方案测试：`154/154` 通过。
- 格式检查：通过。
- 文档本地链接检查：通过。
- Git 差异空白检查：通过。
- 暂存内容敏感信息检查：通过。
- 正式信任包验收活动档案：`4,754` 个逻辑文件、`4,754` 个对象、`621,732,083` 字节全部通过。
- 活动档案清单 SHA-256：`0E059BBFE9FAB6770204DE547567CA64420A45E8364FA93206BB316E8AE2B69F`。
- 使用最终 EXE 对生产 OSS 复验：上传 `0` 个对象、跳过 `4,754` 个对象、上传 `0` 字节。
- 线上当前对象全部匹配本地长度和 `x-oss-meta-sha256`，本次复验没有创建新对象版本。

## 回滚

`0.7.0` 只改动管理员电脑上的发布工具和发布 RAM 最小权限，不改启动器、API、数据库、Velocity 或 Minecraft 服务。需要回滚时可停止使用 `0.7.0` 并恢复历史 `0.6.0` 归档，但在版本控制 Bucket 上重新使用 `0.6.0` 可能生成重复对象版本，因此只应作为诊断回退，不能继续执行正式 OSS 上传。

RAM 策略 v2 的 `oss:GetObject` 只用于元数据校验。若回退该权限，`0.7.0` 会因无法执行 `HeadObject` 而安全失败，不会退化为盲目覆盖。

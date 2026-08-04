# Publisher 1.0.0 正式发布

- 制品源码提交：`f0616a69e95a6dd6ff172369a4bb8883e4e6ab0b`
- 正式标签：`publisher-v1.0.0`
- 产品版本：`1.0.0+f0616a69e95a6dd6ff172369a4bb8883e4e6ab0b`
- 生产范围：独立 Package Publisher Agent；既有手工签名与恢复命令继续保留

## 发布范围

`1.0.0` 增加隔离的 `run-package-agent` 模式。代理只领取已经由管理员确认且仍持有有效
租约的整合包任务，在同一 Windows 管理账号的 DPAPI 边界内读取代理令牌、生产签名
私钥和独立 OSS 发布凭据。API 不接收这些秘密，只接收发布结果并用内嵌生产公钥再次
验签。

代理对内容寻址对象执行不可覆盖发布：已存在对象必须同时匹配长度和 SHA-256 元数据；
缺失对象上传后再次读取元数据。客户端发布固定进入 `Test`，不能推进 Gray 或
Production。

## 正式制品

| 制品 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `Hechao-Publisher-1.0.0-win-x64.zip` | `33,374,660` | `99B354A320B03F5FC09CC571D65C3E01F30DEF1236735628CD73605123D625CC` |
| `Hechao.Publisher.exe` | `74,534,899` | `A924AC39B639B143356C6EC3EB6D77E9F75F8D1DA7BE96B8C8BE81E1F3DC81EA` |

ZIP 只有 `Hechao.Publisher.exe` 一个条目，不包含配置、令牌、私钥、OSS 凭据、PDB 或
日志。归档内 EXE 与生产安装文件哈希一致。

## 生产部署与验证

- 计划任务：`Hechao Launcher Package Publisher Agent`，最终为单实例 `Running`；
- 正式路径：`C:\ProgramData\Hechao\PackagePublisherAgent\Hechao.Publisher.exe`；
- 安装时创建 `backup-20260803T180927Z`；这是首次代理安装，没有旧代理二进制；
- 最终数据库心跳版本为 `1.0.0`，复核时年龄约 `8.3` 秒；
- Publisher 专项测试 `39/39`，完整解决方案 `633/633`。

固定试包最终复核并跳过 `4` 个已存在对象，上传新对象 `0` 个；API 二次验签得到清单
SHA-256 `9a6938025ad7e2c620d87e83579e669c27ca8676d79e07798694c2b542af7f50`。
测试档案只有 `Test=100%`，Gray 和 Production 均无发布指针。代理没有控制游戏服。

## 回滚

异常时先停止 Publisher 计划任务，再关闭 API 的整合包导入开关。首次安装没有旧代理
版本可切换；回滚应保留 DPAPI 文件和状态目录供审计，不删除已经产生的不可变 OSS
对象。API、客户端正式通道和游戏服务端不会因停止 Publisher 自动变化。

结构化证据见
[`evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json`](evidence/PACKAGE_IMPORT_PRODUCTION_ACCEPTANCE_2026-08-05.json)。

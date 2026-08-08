# Publisher CLI 1.3.0

> 状态：已用于启动器 `0.15.0` 正式不可变发布；常驻 Publisher Agent 保持 `1.2.1`。

## 变更

`upload-launcher-release` 新增 Linux `systemd-credentials` 输入，同时保留 Windows
DPAPI 输入。两种模式进入同一不可变上传器，继续校验对象键、长度、SHA-256、元数据、
私有 ACL、禁止覆盖和短时签名读取。

## 制品与验证

| 项目 | 值 |
| --- | --- |
| RID | `linux-x64` |
| 类型 | 自包含单文件 |
| 大小 | `74,645,019` 字节 |
| SHA-256 | `6DF3BAC532E1BD40E45B72424E895E6CE9EFCA79294A0CB5A3072120B8686B8F` |
| 正式标签 | `publisher-v1.3.0` |

- Publisher 测试 `55/55`，完整解决方案 `704/704`，零编译警告。
- 正式制品在阿里云受限暂存目录逐字节复核并显示版本 `1.3.0`。
- 一次性 `systemd-run` 以 `hechao-publisher` 身份加载现有加密 OSS 凭据；凭据明文
  只存在于 systemd 私有运行时挂载，不进入参数、日志、Git 或证据。
- 启动器 `0.15.0` 首次上传成功，第二次确认对象一致后跳过，两轮签名回读 `200`、
  两轮匿名读取 `403`。
- 常驻 `/opt/hechao-package-publisher/current` 未切换，服务未停止或重启，PID
  `1459607`、`NRestarts=0`。

## 回滚

该 CLI 只用于一次性发布，不替换常驻 Agent，因此无需服务回滚。上传对象不可覆盖；
只有 Launcher API 更新通道引用该版本后玩家才能发现安装包。

# Velocity Authorizer 0.5.0 正式发布

- 源码提交：`e7fd989a5edacfafba3999ecf8b7ccef183b7e19`
- 正式标签：`velocity-authorizer-v0.5.0`
- 生产模式：`monitor`
- 直接回滚版本：`0.4.0`

## 生产行为

- 授权响应可携带受控的 `backendHost` 与 `backendPort`；
- 首次授权到尚未静态登记的独立槽时，插件按 API 批准的回环地址动态注册目标并路由；
- 只接受 `127.0.0.1` 或 `::1`，拒绝外部地址、不完整字段、非法目标名和既有地址不一致；
- 固定服务器继续使用 `velocity.toml` 的静态登记，大厅隔离、一次性授权和客户端兼容
  门禁保持不变。

## 制品与部署

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `HechaoVelocityAuthorizer-0.5.0.jar` | 24,782 字节 | `15099474278E1A54E41A173232BD3A72B256583C1EC5A55CCDD1629261C76176` |

- 正式路径：`E:\Velocity\plugins\HechaoVelocityAuthorizer-0.5.0.jar`；
- 综合回滚备份：
  `C:\ProgramData\Hechao\backups\server-slot-families-pre-0.7.0-20260815T055857Z`；
- 部署前后已建立连接均为 `0`；
- Velocity PID 从 `3020` 变为 `4644`，启动日志确认插件 `0.5.0`、`monitor` 和
  `25577` 监听，无 ERROR；
- 固定活动服 PID `4400`、大厅 PID `7328` 未变化，工业季继续停止且 `25600` 无监听。

## 验证

- Gradle 测试 `31/31` 通过，覆盖数字解析、回环地址、动态注册、并发既有注册检查、
  外部地址拒绝和首次故障关闭；
- API `0.32.0` 健康，工业季槽数据库路由为
  `Survival / 25600 / activity-survival / Ready`；
- 本次只重启 Velocity，没有操作任何 Minecraft 服务端。

## 回滚

在零玩家连接窗口停止 `Codex-Velocity-Live`，从综合备份恢复 `0.4.0` JAR、配置与
`velocity.toml`，确认插件目录只保留一个 Authorizer JAR 后重启 Velocity。`0.4.0` 不支持
独立动态目标，因此回滚期间独立槽必须保持停止和关闭；不得把玩家送入大厅作为回退。

结构化证据见
[`evidence/VELOCITY_AUTHORIZER_0.5.0_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/VELOCITY_AUTHORIZER_0.5.0_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

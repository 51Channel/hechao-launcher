# 赫朝启动器 API 0.38.0 多 Java 部署候选

## 范围

- 整合包确认后，把分析器得到的 `javaMajorVersion` 写入 `DeployPackage` 命令；
- 后台直接部署与活动企划部署两条路径使用同一字段；
- `ServerPackageDeploymentRequest.JavaMajorVersion` 保持可空，旧命令、租约重放和旧代理
  仍可反序列化，缺失值不会被错误解释成 Java 21；
- API 不选择本机 Java 路径，运行时目录继续只由对应 VPS 管理。

## 发布顺序

1. 确认整合包与服控操作队列为空，发布窗口内暂停新的整合包确认；
2. 在 owl5 配置并核验 `HECHAO_JAVA_21_HOME` 与 `HECHAO_JAVA_8_HOME`；
3. 先升级 ServerControlAgent `0.8.0`，只核对代理心跳、目标和既有游戏 PID；
4. 再发布 API `0.38.0`，随后才恢复整合包确认；
5. 用停止的隔离槽验证 Java 8 标记、受管启动失败关闭和 Java 21 旧标记回退。

API `0.37.0` 与 Agent `0.8.0` 的短暂混合窗口禁止提交新部署，因为旧 API 不会发送
Java 主版本。API 可以先回滚到 `0.37.0` 并关闭整合包导入；一旦存在依赖专用 Java 的
部署标记，不得把 owl5 Agent 降回 `0.7.2`。

## 当前验证

- API：`383` 通过，`1` 项需要外部 PostgreSQL 的集成测试按环境跳过；
- ServerControlAgent：`79/79`；
- 完整解决方案：`833` 通过，`1` 项外部 PostgreSQL 条件测试跳过；Release 构建
  `0` 警告、`0` 错误；
- PowerShell 受管启动：`12/12`，覆盖显式 Java 8、专用运行时失效时失败关闭与旧标记
  默认回退；
- 标准整合包模板：通过，包含旧 Forge 根级 JAR 启动合同；
- PowerShell 7：`49/49` 脚本解析通过；既有发布台账：`26/26`；本次 C# 差异格式和
  `git diff --check` 通过；
- 全仓 `dotnet format` 仍被基线中未改动的
  `tests/Hechao.Launcher.Tests/MinecraftRunningStateStoreTests.cs` 旧缩进阻断，本候选没有
  把该无关文件混入改动；正式发布制品与生产健康检查仍待发布阶段执行。

## 当前状态

这是未部署候选。生产仍为 API `0.37.0`；本记录不授权启动、停止、重启或开放任何
Minecraft 服务端。

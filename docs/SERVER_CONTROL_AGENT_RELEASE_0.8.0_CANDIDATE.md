# ServerControlAgent 0.8.0 多 Java 运行时候选

## 范围

- 部署标记新增可选 `javaMajorVersion`，值来自 API 已确认的整合包分析结果；
- 受管 runner 对显式版本只使用 `HECHAO_JAVA_<版本>_HOME`，变量缺失、版本越界、
  标记无效或 `bin\\java.exe` 缺失时均在启动脚本前失败关闭；
- 不含版本字段的旧标记和旧命令继续使用 `HECHAO_JAVA_HOME`；
- owl5 固定模板增加标准 `world/world_nether/world_the_end` 保留路径，未来动态槽更新时
  可以安全保留标准世界；旧 `airship_escape*` 路径继续保留；
- 旧 Forge 校验允许唯一根级 `forge-*.jar` 通过 `-jar` 启动，现代 Forge 的
  `win_args.txt` 合同保持不变。

## 部署门槛

- `HECHAO_JAVA_HOME` 保持现值，避免旧标记行为变化；
- `HECHAO_JAVA_21_HOME` 必须指向当前已核验的 owl5 Java 21；
- `HECHAO_JAVA_8_HOME` 必须来自可信 Temurin Java 8，并核验来源、Authenticode、版本、
  SHA-256 与 `bin\\java.exe`；不得直接信任上传包自带 runtime；
- 升级只替换 Agent、runner 和无秘密配置，不能启停 Minecraft 或 Velocity；
- 发布前后记录全部既有 Java PID、启动时间和监听端口，任何变化立即回滚代理文件。

## 当前验证

- ServerControlAgent：`79/79`；
- API：`383` 通过，`1` 项环境集成测试跳过；
- 完整解决方案：`833` 通过，`1` 项环境集成测试跳过；Release 构建 `0` 警告、
  `0` 错误；
- PowerShell 受管启动：`12/12`，其中专用运行时路径无效时确认未执行服务端启动脚本、
  未创建运行标记，也未回退默认 Java；
- PowerShell 7：`49/49`；既有发布台账：`26/26`；本次 C# 差异格式与
  `git diff --check` 通过；
- 旧 Forge 包源合同与模板测试通过；
- 商业街 Forge `1.12.2` 使用 Temurin `8u502` 的隔离冷启动、协议 `340`、玩法
  `SELFTEST PASS`、`save-all` 和正常停服均通过，日志错误为 `0`。

## 回滚边界

若尚未部署任何显式 Java 8 标记，可恢复 `0.7.2` EXE、runner、配置和动态槽状态后只
重启代理。若 Java 8 标记已经存在，先关闭整合包导入并保留 Agent `0.8.0`；不能让旧
runner 以默认 Java 21 启动 Forge 1.12.2。

当前未部署生产，也未启动、停止或重启任何游戏服。

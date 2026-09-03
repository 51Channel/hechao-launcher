# ServerControlAgent 0.8.1 正式发布

- 发布日期：`2026-09-03`
- 源码提交：`14adba190158b1f84d1aa5946965059a3a93e015`
- 生产模板对齐提交：`3150720c8b4bad0009a90646f359641ba43bdcde`
- 正式标签：`server-control-agent-v0.8.1`
- 部署主机：owl5
- owl9：保持 `0.7.2`，本轮未操作

## 功能范围

`0.8.1` 包含 `0.8.0` 的多 Java 运行时能力，并修复旧 Forge 设置心跳：

- 部署标记可保存 `javaMajorVersion`，显式版本只使用受管
  `HECHAO_JAVA_<版本>_HOME`；路径无效或版本不受支持时失败关闭；
- 没有版本字段的既有部署继续使用 `HECHAO_JAVA_HOME`，不改变旧目标；
- 旧 Forge 根级 `forge-*.jar` 的唯一 `-jar` 启动合同可通过受管校验；
- Minecraft `1.12.2` 缺少 `simulation-distance` 时，以 `view-distance` 作为只读展示值；
- 数字难度 `0..3` 映射为标准名称，设置写回时继续保留旧版数字形式；
- 旧版设置写回不会凭空加入不支持的 `simulation-distance` 字段。

owl5 的动态槽模板现与生产一致：主机固定文件只有 `forwarding.secret`，世界保留路径只有
`world/world_nether/world_the_end`。这样不会把固定活动服遗留的 Paper 配置或旧飞艇世界名
传播到 Forge、Fabric、NeoForge 等独立槽。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.8.1-20260903T101920Z-win-x64.zip` | `33,255,103` 字节 | `2394345A9AB2B70C48A8328E062C57AF274DB5072CD10726751AEA5A27F0FC6C` |
| `Hechao.ServerControlAgent.exe` | `74,195,551` 字节 | `6A36645D82F3FF40BE020E6EE8E13ED43E657B84D54F4A3C252561E95331C402` |

产品版本为 `0.8.1+14adba190158b1f84d1aa5946965059a3a93e015`。ZIP 只包含单文件
EXE，解压后二次摘要一致。

## 备份与生产验收

- 回滚点：
  `C:\ProgramData\Hechao\backups\server-control-agent-pre-0.8.1-20260903T102131Z`；
  共 `5` 个文件、`74,206,019` 字节；
- 上一生产 EXE 为 `0.8.0+4838bc303fad960db9f9b8822c6061b9c297932c`，SHA-256 为
  `AE2CC63686943037A20EC8808CF8F5CDE567AAE9E982CE045BDF597A044B4380`；
- Agent 专项 `82/82`、API `383/383`、完整解决方案 `836` 通过、`1` 项外部
  PostgreSQL 条件测试跳过；Release 构建 `0` 警告、`0` 错误；
- owl5 计划任务为 `Running`，生产 EXE 版本、长度和 SHA-256 全部匹配；最终只读复核
  Agent PID 为 `152`；
- owl5 的 `10/10` 目标心跳新鲜且版本为 `0.8.1`；owl9 的 `2/2` 目标继续由
  `0.7.2` 新鲜上报；
- 商业街心跳正确返回 `Xms=1024 MiB`、`Xmx=6144 MiB`、`maxPlayers=24`、
  `viewDistance=10`、`simulationDistance=10`、`difficulty=easy`；
- 升级前后 owl5 Java PID 均为 `4500 / 5040 / 5160`，分别对应既有独立服、Velocity
  和内部大厅；商业街任务为 `Ready`，`25602` 无监听；
- 本轮只重启 Agent 自身计划任务，没有启动、停止、重启任何 Minecraft 或 Velocity，
  也没有发送游戏控制台命令。

## 回滚

若只需撤销旧版设置心跳兼容，可在 API、命令和导入队列为空时停止 Agent 计划任务，恢复
上述备份中的 `0.8.0` EXE、配置和动态槽状态，再启动 Agent 并核对全部心跳；此操作不得
启停游戏服。商业街已有显式 Java 8 部署标记，因此不能降回 `0.7.2`。`0.8.0` 不创建
正式标签，长期修复应以前滚方式完成。

结构化证据见
[`evidence/SERVER_CONTROL_AGENT_0.8.1_PRODUCTION_DEPLOYMENT_2026-09-03.json`](evidence/SERVER_CONTROL_AGENT_0.8.1_PRODUCTION_DEPLOYMENT_2026-09-03.json)。

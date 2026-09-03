# 赫朝商业街建筑对决 1.0.0 Test 发布与停止槽部署

- 发布日期：`2026-09-03`
- 档案 ID：`minigame-commercial-street-forge-1.12.2`
- 服务器 ID / 槽：`minigame-commercial-street / Minigame`
- Minecraft：`1.12.2`
- Forge：`14.23.5.2859`
- Java：`8`
- 正式标签：`profile-minigame-commercial-street-forge-1.12.2-v1.0.0`

## 标准化来源

用户交付的 `商业街建筑对决交接.zip` 是外层交接容器，SHA-256 为
`7F0889734DB894AD711642D1D1CC986DA748D71F6DA5434A9F47CAD9F81F8A9A`。后台不会递归
识别其中再次嵌套的客户端和服务端 ZIP，因此旧任务
`adf0e962-e79b-411e-9b63-011fc94193b6` 保持 `AwaitingReview`，没有推进或删除。

正式导入使用根级 `hechao-pack.json + client + server` 标准包：

| 项目 | 值 |
| --- | --- |
| 本地制品 | `H:\MCMOD\artifacts\package-import\minigame-commercial-street-forge-1.12.2-1.0.0.zip` |
| 大小 | `465,456,939` 字节 |
| SHA-256 | `BA59F103599ADBEFFE9CB5EB706732936B2616579D1D7B04430CE3F8FC76BBD2` |
| 文件 | `1,475` 个；展开后 `465,156,465` 字节 |
| 客户端 | `1,405` 个文件；`352,494,958` 字节 |
| 服务端 | `69` 个文件；`112,661,170` 字节 |

生产分析结果为 `Canonical`，准确识别 Forge、Java 8、客户端和服务端，问题和阻断均为
`0`。客户端、服务端共同的 `10` 份 JAR 已逐文件核对摘要。

## 客户端 Test 发布

- 导入 ID：`26837294-6723-4394-a8b1-1a23fd392df7`；最终状态 `Completed`；
- 清单 SHA-256：
  `43C1550E21D95AA89E61F6518BA435DF342EB34ABF46D57D62FEE34367AE04EE`；
- Publisher `1.2.1` 新增 `15` 个对象、`12,952,949` 字节，校验后复用 `1,387` 个对象；
- Test 为 `1.0.0 / 100% / r2`；Gray 和 Production 均未分配发布；
- 档案未暂停，目录最低等级为 `Participant`；目录保持隐藏且状态为 `Closed`。

## 服务端停止部署

- 独立槽：`E:\HechaoActivitySlots\minigame-commercial-street`；
- 计划任务：`Hechao-Server-minigame-commercial-street`；内部端口 `25602`；Velocity 目标
  与服务器 ID 相同；冲突组为空；
- 平台生成的服务端归档为 `112,672,330` 字节，SHA-256 为
  `E21541D0E595C23FF636343A59B5F42CEB5C3C9E6601585F7DE80CD9C3FDE185`；
- 部署操作 `8c1da871-cb70-4965-98bc-1deda8eb36a7` 返回
  `PACKAGE_DEPLOYED_STOPPED`，没有自动停止其他服务端；
- 部署标记固定 `javaMajorVersion=8`、`Xms=1024 MiB`、`Xmx=6144 MiB`，并明确
  `preserveWorldData=false`；
- 旧版数字难度 `1` 已由 Agent `0.8.1` 正确显示为 `easy`；旧版没有
  `simulation-distance`，后台按视距显示 `10`，写回不会新增不兼容字段；
- 计划任务为 `Ready`，进程为空，`25602` 监听数为 `0`。该任务从未启动；部署前后
  owl5 的 Java PID 均为 `4500 / 5040 / 5160`。

## 开放门禁

本次只授权 Test 下载和停止槽部署，不授权启动或玩家开放。以下项目完成前，目录必须继续
隐藏并保持 `Closed`，Gray/Production 不得分配：

- 为 Forge `1.12.2` 提供并验证生产 Velocity modern forwarding 兼容实现；
- 分配独立语音 UDP 映射，或明确关闭 Simple Voice Chat；
- 补齐 Forge `1.12.2` 的 TPS/MSPT/GC 深度指标；
- 归档第三方模组官方来源、适用版本和许可证；
- 完成正式世界备份与隔离恢复；
- 使用正确/错误客户端完成 fresh grant、拒绝路径和后端不可直连验证；
- 按 `2/3/5/20` 人完成玩法、性能、断线重连和回滚灰度。

隔离环境中的 Forge 冷启动、协议 `340`、玩法 `SELFTEST PASS`、`save-all` 和正常停止只证明
包本身可运行，不替代上述生产门禁，也不授权临时关闭正版验证或绕过 Velocity。

## 2026-09-04 启动故障修复

用户后续明确要求启动该服。首次后台启动返回 `START_TIMEOUT`，根因是 owl5 重启后没有
Administrator 桌面会话，而动态槽计划任务仍使用 `InteractiveToken`。ServerControlAgent
`0.8.2` 已把动态槽启动任务改为无密码 `S4U` 并完成真实安装类型校验；Minecraft 控制台桥
也使用 `S4U`，避免同一条件下命令超时。

修复后商业街以 PID `2948` 在 `127.0.0.1:25602` 运行，协议探针返回
`1.12.2 / 340 / 0/24`，日志出现 `Done (3.235s)`，启动错误、FATAL、崩溃和 OOM 均为
`0`。`street selftest` 返回 `SELFTEST PASS`，控制台 `list` 成功且队列为零；API 心跳已同步
`reported_online=true`。修复前的失败操作继续作为审计历史保留。目录仍隐藏并保持
`Closed`，Gray/Production 与其余开放门禁没有改变。完整记录见
[`SERVER_CONTROL_AGENT_RELEASE_0.8.2.md`](SERVER_CONTROL_AGENT_RELEASE_0.8.2.md)。

## 回滚

客户端回滚先取消 Test 对 `1.0.0` 的分配，保留不可变清单和 OSS 对象。服务端回滚必须在
任务停止、端口无监听、目录隐藏且 `Closed` 的前提下，使用受控删除或后续整合包原子替换；
不允许手工启动验证。原始交接包、标准包、平台服务端归档和部署标记的摘要必须分别核对，
不能混用。

结构化证据见
[`evidence/COMMERCIAL_STREET_PACKAGE_1.0.0_TEST_DEPLOYMENT_2026-09-03.json`](evidence/COMMERCIAL_STREET_PACKAGE_1.0.0_TEST_DEPLOYMENT_2026-09-03.json)。

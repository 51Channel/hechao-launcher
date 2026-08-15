# ServerControlAgent 0.7.0 正式发布

- 源码提交：`e7fd989a5edacfafba3999ecf8b7ccef183b7e19`
- 正式标签：`server-control-agent-v0.7.0`
- 部署主机：owl5；owl9 保持 `0.4.0`

## 功能范围

- 动态槽支持生存、活动、PVP、小游戏四种用途和对应 ID 前缀；
- 独立槽使用 `25600-25611` 中的唯一端口、空冲突组和自身 Velocity 目标；
- 创建前同时检查静态目标、动态状态和本机 TCP 监听，失败不落盘并自动回滚；
- 固定 `activity / 25568 / owl5-activity-slot` 继续作为旧替换入口模板；
- 新槽默认停止、隐藏且要求先部署整合包，代理不会自动启动 Minecraft。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.7.0-20260815T055857Z-win-x64.zip` | 33,253,888 字节 | `F017761314DDCC14E5166CDD268D0824CB2B4BBB0164BD0AED0C3995FF14E53C` |
| `Hechao.ServerControlAgent.exe` | 74,191,455 字节 | `725A61CFE0344B8E51130553F3576C4312C7F700E1F643137B8710CA191F5F81` |

ZIP 只包含一个 EXE，产品版本为
`0.7.0+e7fd989a5edacfafba3999ecf8b7ccef183b7e19`，不含配置、凭据或 PDB。

## 生产迁移

- 只停止并重启 `Hechao Launcher Server Control Agent` 计划任务；
- 显式配置端口池为 `25600-25611`；
- 保留旧 ID `activity-survival`，将用途从 `Activity` 改为 `Survival`，端口从
  `25568` 改为 `25600`，Velocity 目标改为 `activity-survival`，冲突组清空；
- 当前与受控回滚目录中的 `server.properties` 均改为 `server-port=25600`；
- 数据库目录记录保持 `Closed / hidden`，`.hechao-deployment.json` 哈希未变化；
- 最终代理 PID `5512`，八个 owl5 目标均以 `0.7.0` 新鲜上报。

生产备份为
`C:\ProgramData\Hechao\backups\server-slot-families-pre-0.7.0-20260815T055857Z`，
共 `20` 个文件，校验清单 SHA-256 为
`B3095C71AD3A4A13E916CCF9E633AE6499095DD461271F8D931ABB996910BAE7`。
其中包含旧代理、配置、动态状态、工业季关键文件、计划任务、Velocity 插件与配置；不含
明文令牌。

## 验收边界

- 工业季计划任务保持 `Ready`，`25600-25611` 没有监听；
- 固定活动服 PID `4400`、启动时间 `2026-08-15T04:26:50Z` 和 `25568` 监听未变化；
- 内部大厅 PID `7328` 未变化；Velocity 按单独发布升级，不属于 Minecraft 服务端；
- 代理新版本启动后无新增错误，活动槽身份标记未变化；
- 本次没有启动、停止、重启或切换任何 Minecraft 服务端。

## 回滚

确认没有进行中的服控命令后，仅停止服控代理任务。先把数据库中的工业季槽、服控目标和
目录记录恢复为 `Activity / 25568 / activity / owl5-activity-slot`，再从上述备份恢复代理
EXE、配置、动态状态和两份 `server.properties`，最后启动旧代理 `0.6.0` 并核对心跳。
工业季必须全程保持停止，不能用启动服务端验证回滚。

结构化证据见
[`evidence/SERVER_CONTROL_AGENT_0.7.0_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/SERVER_CONTROL_AGENT_0.7.0_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

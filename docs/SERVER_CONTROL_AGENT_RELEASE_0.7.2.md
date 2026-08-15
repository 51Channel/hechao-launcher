# ServerControlAgent 0.7.2 正式发布

- 源码提交：`0de40dbd30e41bf42b29ff112f947a95f7821901`
- 正式标签：`server-control-agent-v0.7.2`
- 正式制品时间：`20260815T073034Z`
- 生产主机：owl5、owl9

## 发布范围

- 双机所有固定目标与 owl5 动态槽统一使用 `allowedCommandPrefixes=["*"]`；
- 放行 `op/deop`、LuckPerms、命名空间命令以及其他游戏和插件命令；
- `stop/restart/shutdown/end` 仍由本机代理拒绝，必须使用结构化服控按钮；
- 禁用动态槽的主机在动态状态为空时不再规范化空根目录，owl9 可安全运行新版。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.7.2-20260815T073034Z-win-x64.zip` | 33,253,487 字节 | `85517C013888E28593AB1BFA97E6D14ADFB0C36E0ABF3D728E6BEF570520C149` |
| `Hechao.ServerControlAgent.exe` | 74,191,455 字节 | `908F9B115D8717B7919930570BDAAF4360567C1809972A0236D028B07B43607C` |

ZIP 只含一个 EXE，解压后二次哈希一致。产品版本为
`0.7.2+0de40dbd30e41bf42b29ff112f947a95f7821901`。

## 测试与升级处理

- Agent `76/76`、API `350/350`、完整解决方案 `790/790`；
- Playwright `33/33` 覆盖 `op 51Channel` 与生命周期命令提示；
- 第一次 owl5 `0.7.1` 安装遇到 Windows 文件替换 API 兼容错误，脚本立即恢复
  `0.7.0`；改用同卷覆盖后成功；
- owl9 首次运行 `0.7.1` 时发现禁用动态槽仍解析空根目录，脚本恢复
  `0.4.0` 和旧配置；`0.7.2` 修复并新增精确回归测试；
- `0.7.1` 不创建正式标签。

## 备份与生产验收

- owl5 最终升级备份：
  `C:\ProgramData\Hechao\backups\server-control-agent-pre-0.7.2-20260815T073034Z`，
  4 个文件、74,200,151 字节，清单 SHA-256
  `A6657086309D8232666722F6E5C15D549F16AB2717DC6B191B19EB0F6F57D510`；
- owl9 最终升级备份：
  `C:\ProgramData\Hechao\backups\server-control-agent-pre-0.7.2-20260815T073034Z`，
  3 个文件、74,114,406 字节，清单 SHA-256
  `276A5DD24EF44D1785E75EDC1DDF780DB55898D5C2707D9A391653A1A5F66B54`；
- 最终 Agent PID：owl5 `5512`、owl9 `3444`，两台计划任务均为 `Running`；
- API 中 `10/10` 目标版本均为 `0.7.2`、前缀均为 `*`、心跳均小于 30 秒；
- owl5 游戏进程保持 Velocity `4644 / 25577` 与大厅 `7328 / 25566`；动态端口
  `25600-25611` 无监听；
- owl9 升级前后 Java 进程均为 `0`；双机新 Agent 日志错误为 `0`，临时文件为
  `0`；
- 全程只重启 Agent 计划任务，服控操作和 Minecraft 控制台命令均为 `0`。

## 回滚

单机程序回滚可停止且只停止 `Hechao Launcher Server Control Agent`，恢复对应
`pre-0.7.2` 目录中的 EXE、配置和动态状态后重启代理。若要完整回到本次功能前：

- owl5 使用
  `server-control-agent-pre-0.7.1-20260815T071021Z-retry1` 恢复 `0.7.0` 和旧前缀；
- owl9 使用 `server-control-agent-pre-0.7.2-20260815T073034Z` 恢复 `0.4.0` 和旧前缀；
- 两台旧心跳恢复后，才可把 API 回滚到 `0.32.0`。

回滚不需要也不得重启任何 Minecraft 服务端。结构化证据见
[`evidence/SERVER_CONTROL_AGENT_0.7.2_PRODUCTION_DEPLOYMENT_2026-08-15.json`](evidence/SERVER_CONTROL_AGENT_0.7.2_PRODUCTION_DEPLOYMENT_2026-08-15.json)。

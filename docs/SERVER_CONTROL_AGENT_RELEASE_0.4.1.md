# ServerControlAgent 0.4.1 正式发布

- 制品源码提交：`fb6b9975d6ecea62533f002ab473e8c66b4e7cad`
- 正式标签：`server-control-agent-v0.4.1`
- owl5 部署时间：2026-08-06 21:06 CST
- owl9 保持 `0.4.0`，本轮未升级

## 功能与边界

- 通过 Windows `GlobalMemoryStatusEx` 读取真实物理内存并随心跳上报；读取失败时返回
  空值，不伪造容量；
- 固定 `activity` 活动槽不再受旧 `8192 MiB` 人工配置限制，以 VPS 真实物理容量和
  `64 GiB` 技术边界校验结构化部署命令；
- 其他服务端继续使用各自原有的单服内存上限，本版本不会改写 JVM 参数；
- 继续保留停服检查、目标目录门闩、原子目录切换、失败回滚和删除能力边界。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.4.1-20260806T125215Z-win-x64.zip` | 33,222,552 字节 | `BEA41CD1386C7098853CFEC2A60270E307FDF8C3243160D5CB538B5980AA6FE7` |
| `Hechao.ServerControlAgent.exe` | 74,109,535 字节 | `EAB5360968B79479726568FA439E957BBEEACFA617284475679380C5838C7D78` |

EXE 产品版本为
`0.4.1+fb6b9975d6ecea62533f002ab473e8c66b4e7cad`。

## 生产验收

- owl5 计划任务保持 `Running`，7 个目标和活动槽部署能力保持；
- 主机物理内存为 19,326,763,008 字节，心跳落库为 `18431 MiB`；
- API 计算的推荐最小值为 `4096 MiB`，推荐最大值为 `8960 MiB`；
- 升级前后 Java PID `2576/6008/7748/9428/10412` 的 PID、启动时间和可执行路径
  完全一致；
- ServerControlAgent `56/56`、完整解决方案 `.NET 680/680`；
- 本次只停止并恢复代理自己的计划任务，没有启动、停止、重启或控制 Minecraft、
  Velocity 和 Publisher；
- owl5 临时上传和解压文件已清理，安装回滚备份完整保留。

回滚目录：

`C:\ProgramData\Hechao\backups\server-control-agent-20260806T130640Z`

owl9 继续运行 `0.4.0`，其既有回滚目录和游戏进程均未触碰。

结构化证据见
[`evidence/PACKAGE_MEMORY_GUIDANCE_PRODUCTION_DEPLOYMENT_2026-08-06.json`](evidence/PACKAGE_MEMORY_GUIDANCE_PRODUCTION_DEPLOYMENT_2026-08-06.json)。

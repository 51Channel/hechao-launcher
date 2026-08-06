# ServerControlAgent 0.4.2 正式发布

- 源码提交：`7ecfddffec8cdc3bd18eafdd588f4f1c7eedda39`
- 部署主机：owl5
- owl9 保持原版本，本轮未操作

## 修复

- 删除服务端目录前，把 `forwarding.secret` 等主机固定文件写入独立原子快照；
- 当活动槽目录不存在且 `preserveWorldData=false` 时，从快照恢复主机固定文件并部署；
- 当 `preserveWorldData=true` 且目录不存在时继续失败关闭，禁止伪造世界恢复；
- 快照拒绝重解析点、路径逃逸和缺失的必需文件；目录切换失败时保持活动槽缺失并清理受控暂存。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-server-control-agent-0.4.2-20260807T0015-win-x64.zip` | 33,226,836 字节 | `D2C1EDA847516875675738A9F61FC7980CDCDF4154DC28D2063DD1F6197B27A1` |
| `Hechao.ServerControlAgent.exe` | 74,122,335 字节 | `D29F6504B69CE01ABEE0F5323E1BEC9AEE044203AF9F7AC266B5CB24A19C19B6` |

产品版本为 `0.4.2+7ecfddffec8cdc3bd18eafdd588f4f1c7eedda39`。定向测试
`57/57` 通过。

## 生产验收

- owl5 代理计划任务为 `Running`；升级回滚目录为
  `C:\ProgramData\Hechao\Backups\server-control-agent-20260806T161917Z`；
- 快照只允许 `SYSTEM` 与本机管理员访问；备份候选与当前 Velocity 文件仅通过
  SHA-256 比对，未读取或记录正文；
- 升级前后 Java PID `2576/6008/7748/9428/10412`、创建时间和路径完全一致；
- 活动目录已重新部署，计划任务保持 `Ready`，`127.0.0.1:25568` 未监听；
- 本轮没有启动、停止或重启 Minecraft 与 Velocity。

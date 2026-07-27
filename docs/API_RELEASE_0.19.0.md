# API 0.19.0 发布记录

> 状态：已生产部署并完成公网回归
>
> 发布 ID：`0.19.0-20260727T005013Z`（历史版本）
>
> 源码提交：`7ba2eba`

## 1. 变更

- 新增迁移 16，保存服务器进程、磁盘、TPS、MSPT、GC 与固定探针问题。
- 新增 30 天幂等运行样本和每 6 小时清理任务。
- 新增管理员端点 `GET /v1/admin/server-runtime/summary`。
- 管理后台增加“服务状态”页，只读展示实时指标和 24 小时问题摘要。
- Windows 状态采集器升级到 `0.2.0`，按本机监听端口读取 Java 进程与数据盘指标。
- 增加 Paper/Purpur 只读指标代理 `0.1.0`，本地原子写入 TPS、MSPT 和累计 GC。

完整数据边界见
[`SERVER_RUNTIME_METRICS_OPERATIONS.md`](SERVER_RUNTIME_METRICS_OPERATIONS.md)。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.19.0-20260727T005013Z.tar.gz` | `45,550,337` | `B8A82819AB0CD42F09A1B435A29CFFC26C6335215157EB8FF5FE1F48B9755455` |
| `Hechao.Api` | `104,346,675` | `29B351C33B6366BF2C3E9263275928D0F5C8329D05C14B1C7A138C0D81B279FA` |
| `hechao-status-collector-0.2.0-win-x64.zip` | `32,011,273` | `30D9BC599B80FEF48D5FE02B340FE494BE8DE7B5D590828BED34F155D81F8167` |
| `HechaoServerMetrics-0.1.0.jar` | `312,161` | `BD03312007E043223B37CF634872C3DAA4C0FB11B80B54ADC546507853528B2C` |

最终 API 归档不含 PDB、环境文件、令牌或外部私钥。第一次生成的
`0.19.0-20260727T004025Z` 候选包含 PDB，发现后使用同一源码和相同程序二进制重新
生成干净归档；最终生产只指向上表候选。

## 3. 自动与隔离验收

- .NET `325/325`、Paper/Purpur 指标代理 `2/2`。
- 管理后台 JavaScript、部署脚本语法和 `git diff --check` 通过。
- 桌面 `1500x860` 与窄屏 `640x900` 浏览器验收无页面横向溢出。
- 生产数据库备份恢复到独立数据库，候选只监听独立端口。
- 迁移 15、16、遥测幂等、运行样本幂等、内存/TPS/磁盘汇总和固定问题摘要通过。
- 签名档案导入、不可变存储、Test/Gray/Production、暂停自动回滚、恢复不自动推广、
  稳定分桶和修订冲突继续通过。
- 临时数据库、目录和 systemd 单元在验收后清理。

## 4. 生产部署

最终切换前统一备份位于：

```text
/var/backups/hechao-unified-account/20260727T005113Z
```

清单 SHA-256 为
`951BCDCE013EF6F64671AC1113D348474A43A0C5FBF3616DF122161DCC724F31`。
PostgreSQL custom dump 为 `134,765` 字节，SHA-256 为
`38C64044B18A77642F76FA534C748459D29989EB38EE18D784028D14B59827C3`，
`pg_restore --list` 成功读取 152 个目录项。

原子切换后：

- `current` 指向
  `/opt/hechao-launcher-api/releases/0.19.0-20260727T005013Z`。
- 安装目录 PDB 数为 0，程序 SHA-256 与候选一致。
- `/healthz`、`/readyz` 报告 `0.19.0` 和 database ready。
- 迁移最大值为 16；目录仍为 6 台服务器、6 个档案、6 个发布和 18 个通道。
- 五个目标继续上报，systemd `NRestarts=0`，部署后 journal 无 warning/error。
- 旧官网、中转 API、管理入口均为 200；错误 Host 下 `/admin/` 为 404。
- 未认证运行指标汇总返回 401，公网 `8090` 不可达。

Windows 采集器 `0.2.0` 已备份旧版本并完成一次手工上报，随后一分钟计划任务返回
`LastTaskResult=0`。部署期间四个 Java PID 保持不变。大厅、Survival1 和 Survival2
已经上报进程内存、CPU、启动时间与 E 盘容量；活动服关闭和 PVP 不可达使用固定问题
代码记录。

指标代理已复制到大厅、Survival1 和 Survival2 的 `plugins` 目录，远端 SHA-256 与
候选一致，备份位于：

```text
E:\manual-backups\server-metrics-20260727T004852Z
```

本次没有启动、停止或重启 Minecraft、Velocity 或任何游戏服。TPS/MSPT/GC 只有在服主
以后自行重启对应 Paper/Purpur 服务端并加载代理后才会出现。

## 5. 回滚

API 直接回滚目标为 `0.18.0-20260726T234852Z`。迁移 16 与历史样本表保留，旧 API
不会读取新增字段。采集器可从
`C:\ProgramData\Hechao\StatusCollector\backups\collector-0.2.0-20260727T004750Z`
恢复。指标代理只在服务端已经关闭时移出，或等待下一次服主计划重启生效。

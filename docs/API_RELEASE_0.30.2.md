# API 0.30.2 正式发布

- 正式发布 ID：`0.30.2-20260811T124943Z`
- 制品源码提交：`bb109e02a5f0b238f94c3b5465be6ce3b47712b3`
- 正式标签：`api-v0.30.2`
- 生产切换时间：2026-08-11 20:57（Asia/Shanghai）
- 数据库迁移：无，保持 `028`

## 发布范围

- 新增仅供官网回环调用的
  `POST /v1/internal/forum/accounts/membership-eligibility`，按统一账号 ID 返回账号启用状态、
  Minecraft UUID、玩家名和验证时间；
- 只有账号启用且三项正版身份字段完整时才返回可申请资格。不存在的账号返回 `404`，空
  UUID 返回输入错误；
- 端点继续复用论坛桥接令牌、回环来源限制和 `internal-forum` 限流，不公开到浏览器，
  不创建账号，也不修改 Minecraft、LuckPerms、白名单或网站成员状态；
- 本次只增加网站成员问卷所需的只读合同，不修改 Launcher、管理后台、活动企划、整合包、
  Publisher 或游戏服控制逻辑。

## 正式制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-launcher-api-0.30.2-20260811T124943Z.tar.gz` | 46,246,030 字节 | `B040932F46A3D3A848D43F5A66F4A004D713FCD854954122EC04EDF21FFABD20` |
| `Hechao.Api` | 105,238,980 字节 | `C861F40ACD39991B248072C4FB17D0F65F1FE9A6F4DA86BF0438EDAA283EA1D5` |

归档共 161 项、156 个文件，只包含单文件 API、静态管理后台与端点清单；本地、上传包、
正式 release 和运行二进制哈希一致。环境文件、PDB、凭据、Cookie、账号明细和签名 URL
未进入制品。

## 测试

- 新增资格模型和受保护路由合同测试；API `.NET 305/305`、完整解决方案
  `.NET 725/725`；
- 生产中 29 个网站映射账号均由新端点返回 `200`，其中 13 个账号满足完整正版资格；
  不存在账号返回 `404`，缺少桥接凭据返回 `401`；
- 验证只记录聚合计数和状态码，没有把账号 ID、玩家名、UUID、Cookie 或桥接令牌写入
  文档和证据。

## 备份与部署

- PostgreSQL custom-format 备份：
  `/var/backups/hechao-launcher/database/hechao-launcher-20260811T125213Z.dump`，
  5,252,813 字节，SHA-256
  `DD93A3FFC7B2F9905D38458D0B765725A2352D7246964841DA9A84EE4937DCF6`；旁车校验与
  `pg_restore --list` 均通过，目录记录 239 行；
- API 环境、systemd、Nginx、当前链接和完整 `0.30.1` release 快照：
  `/var/backups/hechao-launcher/api-predeploy/pre-api-0.30.2-20260811T125215Z.tar.gz`，
  46,381,518 字节，SHA-256
  `399A58662B276ABCC81C3DE0705D519D2E8933B4C8A6F7C78FB4582CD9BF13CC`；
- 原子切换到 `/opt/hechao-launcher-api/releases/0.30.2-20260811T124943Z` 后只重启
  `hechao-launcher-api.service`。Publisher PID 保持 `2064`，未操作 Nginx、Minecraft、
  Velocity、Publisher 或 ServerControlAgent。

## 生产验收

- API `active/running`，PID `2189110`、`NRestarts=0`，只监听
  `127.0.0.1:8090`；回环 `/healthz` 与 `/readyz` 分别返回 `ok`、`ready`、版本
  `0.30.2` 和 `database=ready`；
- 部署后 API warning/error 为 `0`，Publisher PID 未变化；公网网站、活动日历与 Launcher
  后台保持可用；
- API 先于官网成员问卷 release 部署。官网生产资格探针通过后才切换网站，因此不会出现
  新网站调用旧 API 的窗口；
- 自动审批仍关闭。此端点只完成正版资格门禁，不代表 QQ 群接口或自动批准已经启用。

## 回滚

直接程序回滚目标为：

`/opt/hechao-launcher-api/releases/0.30.1-20260810T225350Z`

本版本没有数据库迁移。回滚只需原子恢复 `current` 并重启 API，但新版官网的成员问卷会因
资格端点缺失而受控返回 `503`；回滚期间应暂停成员问卷提交，不能绕过资格检查或改用本地
猜测。

结构化证据见
[`evidence/API_0.30.2_PRODUCTION_DEPLOYMENT_2026-08-11.json`](evidence/API_0.30.2_PRODUCTION_DEPLOYMENT_2026-08-11.json)。

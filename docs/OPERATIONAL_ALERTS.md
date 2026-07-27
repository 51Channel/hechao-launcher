# 统一运行告警

> 当前状态：API `0.20.1`、平台监控器 `0.1.0` 已生产部署
>
> 更新日期：`2026-07-27`

## 1. 边界

统一告警由两部分组成：

- API 每分钟聚合最近 15 分钟的请求指标、认证失败、对象授权失败、客户端下载失败和
  游戏服运行指标，并把活动告警与历史变化写入 PostgreSQL。
- API 主机上的独立监控器每分钟检查公网入口、TLS 证书、异地备份凭据，并把合成事件
  写回 API。监控器只在新告警、级别变化和恢复时发送邮件，避免每分钟重复通知。

监控器没有 SSH 到游戏 VPS、启动 Java、停止 Java 或修改 Minecraft 文件的能力。
服务器离线只产生告警，不触发自动开服。

## 2. 覆盖范围

### API 内部规则

| 指纹 | 触发条件 |
| --- | --- |
| `api:server-errors` | 最近窗口至少 3 次 5xx，或至少 20 个请求且 5xx 率达到 5% |
| `api:latency` | 至少 10 个请求，平均耗时达到 1 秒或单次最大耗时达到 5 秒 |
| `authentication:login-failures` | 最近窗口至少 5 次赫朝账号登录失败 |
| `distribution:client-download-failures` | 至少 5 次安装/修复、至少 3 次失败且失败率达到 20% |
| `distribution:object-endpoint-failures` | 对象下载授权端点至少 2 次 5xx |
| `server:<id>:heartbeat` | 服务器心跳离线、过期或固定探针报告异常 |
| `server:<id>:tick-metrics` | 应有 Paper/Purpur 深度指标但 TPS/MSPT/GC 缺失或过期 |

API 请求指标和告警历史保留 30 天。客户端遥测只接受固定事件、固定阶段、版本和错误
分类，不上传玩家文件内容、OAuth 令牌或异常正文。

### 独立合成检查

- `launcher-api.hechao.world` 的 `/healthz` 与 `/readyz`
- `admin.hechao.world/admin/`
- 私有 OSS 下载根路径预期匿名返回 `403`
- `hechao.world` 与既有 `api.hechao.world`
- 上述五个 HTTPS 主机的证书链与到期时间
- API 公网健康检查延迟
- 最近一次异地数据库备份成功凭据或失败标记

证书剩余 30 天进入 Warning，剩余 7 天进入 Critical。异地数据库备份超过 30 小时
进入 Warning，超过 48 小时进入 Critical；失败标记立即进入 Critical。

## 3. 生产位置

```text
/usr/local/sbin/hechao-platform-monitor
/etc/hechao-platform-monitor/environment
/var/lib/hechao-platform-monitor/state.json
/etc/systemd/system/hechao-platform-monitor.service
/etc/systemd/system/hechao-platform-monitor.timer
```

环境文件保存内部告警令牌和 SMTP 凭据，必须保持 `root:root 600`。API 环境文件只保存
内部令牌的 SHA-256，不保存原令牌。状态文件只包含固定告警字段，不包含凭据。

## 4. 日常检查

```bash
systemctl status hechao-platform-monitor.timer --no-pager
systemctl start hechao-platform-monitor.service
systemctl show hechao-platform-monitor.service \
  -p Result -p ExecMainStatus -p ActiveState
journalctl -u hechao-platform-monitor.service \
  --since today --no-pager
jq . /var/lib/hechao-platform-monitor/state.json
```

管理员后台“运行告警”页显示当前告警、级别、来源、首次/最后出现时间和摘要。告警内部
写入端点只接受固定长度、固定枚举和时间范围内的事件，使用独立内部令牌并受速率限制。

## 5. 生产验收

API `0.20.0` 原子切换后：

- 迁移最大值为 `17`，`/healthz` 与 `/readyz` 公网返回 `200`。
- 平台监控 timer 已启用且每分钟运行，首次状态邮件和后续变化邮件已送达。
- 合成端点、证书和 API 请求告警可写入、更新、恢复并在后台查询。
- 当前活动告警与真实状态一致：活动服和 PVP 离线为 Critical；大厅、Survival1 和
  Survival2 尚未加载指标代理，因此 Tick 指标为 Warning。
- 异地备份首次权限不足产生 Critical，证明失败标记、API 入库和邮件链路均生效。

## 6. 回滚

API 可回滚到 `0.19.0-20260727T005013Z`。迁移 17 的告警表和请求指标表保留，旧 API
不会读取。独立监控器回滚时先停用 timer，再恢复上一份脚本和环境文件；不能删除历史
告警来伪造恢复，恢复必须由下一次正常评估写入。

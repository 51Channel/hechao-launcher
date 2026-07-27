# 平台监控器 0.1.2 发布记录

> 记录日期：`2026-07-28`
>
> 标签：`platform-monitor-v0.1.2`
>
> 构建来源提交：`a3af1f0862853ae50a5e22487c934a0d8fb17a2b`
>
> 状态：已部署生产并完成失败、恢复与邮件转换验收

## 版本边界

`0.1.2` 在 `0.1.1` 的数据库异地备份检查之外，新增论坛与 Sub2API 平台数据备份
检查。监控器分别读取：

```text
/var/lib/hechao-offsite-platform-backup/latest.json
/var/lib/hechao-offsite-platform-backup/failure.json
```

失败标记立即产生 `backup:platform-data-offsite` Critical；成功凭据超过 30 小时产生
Warning，超过 48 小时产生 Critical。监控器仍只在触发、级别变化和恢复时发送邮件，
不会自动启动、停止或重启游戏服。

版本号现在统一写入 HTTP User-Agent 和结构化 `check_complete` 日志，便于直接确认
当前生产脚本。

## 生产制品

生产脚本位于：

```text
/opt/hechao-platform-monitor/hechao-platform-monitor.py
```

文件大小为 `22,518` 字节，权限为 `0555 root:root`，SHA-256 为：

```text
0D81EACDF7E24FC891924907E50905D15D18F8E0CA31E819D8BD05D281171691
```

部署前的 `0.1.1` 脚本保存在同目录的受限回滚副本中，SHA-256 为：

```text
564300F9DBA9B136A847AD985C40F2277B254FE7B76DF48C17F04674A30DF37B
```

生产和临时制品的 `--self-test` 均通过，四个相关 systemd unit 通过
`systemd-analyze verify`。监控 timer 保持 `enabled/active`。

## 生产验收

受控演练写入带明确 drill 标识、退出码 `86` 的平台备份失败标记。`0.1.2` 产生一条
Critical，API 将其记录为 Active，监控日志记录 `transitions=1` 且
`emailDelivered=true`。

随后运行一轮真实平台数据备份。上传、立即回读与逐字节校验成功，runner 自动删除
失败标记。下一轮监控将同一指纹更新为 Resolved，再次记录 `transitions=1` 且
`emailDelivered=true`。演练没有删除告警历史，也没有重启 API、论坛或任何游戏服。

## 回滚

直接回滚目标为 `platform-monitor-v0.1.1`。回滚时先停用监控 timer，恢复
`0.1.1` 脚本并执行 `--self-test`，再重新启用 timer。历史告警和状态文件必须保留，
恢复只能由下一轮真实评估产生。

完整非秘密证据见
[`evidence/PLATFORM_DATA_BACKUP_RAM_V5_ACCEPTANCE_2026-07-28.json`](evidence/PLATFORM_DATA_BACKUP_RAM_V5_ACCEPTANCE_2026-07-28.json)。

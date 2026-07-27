# 平台监控器 0.1.1 发布记录

> 记录日期：`2026-07-28`
>
> 标签：`platform-monitor-v0.1.1`
>
> 构建来源提交：`dc1d1d527d106fbbc41a58add4bfbd7ae2d12cc9`
>
> 状态：生产运行文件已核对，本次只补齐不可变版本与追溯记录

## 版本边界

`0.1.1` 对应已在生产运行的平台监控器脚本。相对 `0.1.0`，它增加异地数据库备份
成功凭据与失败标记的合成检查；尚未启用的论坛与 Sub2API 平台数据异地备份检查不在
该版本中。

生产脚本位于：

```text
/opt/hechao-platform-monitor/hechao-platform-monitor.py
```

文件大小为 `21,265` 字节，SHA-256 为：

```text
564300F9DBA9B136A847AD985C40F2277B254FE7B76DF48C17F04674A30DF37B
```

该哈希与提交 `dc1d1d527d106fbbc41a58add4bfbd7ae2d12cc9` 中对应脚本的原始
Git blob 内容一致。`hechao-platform-monitor.timer` 当前为 `active`。本次核对没有
重启、重载或修改监控服务，也没有操作任何 Minecraft 或 Velocity 进程。

## 回滚

直接回滚目标为 `platform-monitor-v0.1.0`。回滚时先停用监控 timer，再恢复
`0.1.0` 脚本并执行 `--self-test`，最后重新启用 timer。不能删除现有告警历史或状态
来伪造恢复。

当前活动组件的统一来源、哈希、发布人和回滚目标见
[`evidence/ACTIVE_RELEASE_PROVENANCE_2026-07-28.json`](evidence/ACTIVE_RELEASE_PROVENANCE_2026-07-28.json)。

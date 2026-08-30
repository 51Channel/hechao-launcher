# 天域远征幻翼自然生成关闭

- 变更日期：`2026-08-30`
- 目标：owl5 `activity-survival`
- 变更方式：Minecraft 游戏规则热修改，不重启服务器

## 目标与边界

生产服已将游戏规则从：

```text
doInsomnia = true
```

修改为：

```text
doInsomnia = false
```

该规则关闭由玩家长期不睡觉触发的幻翼自然生成，不影响僵尸、骷髅、苦力怕等其他生物的
自然生成。已经存在的幻翼不会被强制清除，命令、刷怪蛋等管理员主动生成方式也未被禁止。

## 执行与持久化

变更通过现有受管控制台桥发送：

```text
gamerule doInsomnia false
save-all flush
gamerule doInsomnia
```

服务器依次明确返回：

```text
Gamerule doInsomnia is now set to: false
Saved the game
Gamerule doInsomnia is currently set to: false
```

随后分别从 `minecraft:overworld`、`minecraft:the_nether` 和 `minecraft:the_end` 查询，三个
标准维度均返回 `false`。`save-all flush` 已把主世界、下界和末地写入磁盘。

首轮事务使用的保存确认窗口只有 2 秒，而实际保存约需 3 秒，因此未在窗口内看到
`Saved the game`。自动回滚随即把规则恢复为 `true` 并再次保存。确认这是等待窗口过短而非
服务器保存失败后，将窗口调整为 30 秒重新执行，最终变更与保存均成功。该过程验证了回滚
路径有效。

## 运行验证

- 计划任务 `Hechao-Server-activity-survival` 保持 `Running`；
- Java PID 修改前后均为 `8348`；
- `127.0.0.1:25600` 始终由同一进程单独监听；
- 修改后 `list` 返回 `5/100`，玩家连接未因本次操作中断；
- 没有重启 Minecraft、Velocity 或服控代理；
- 最终保存造成一次约 `2.3` 秒的瞬时 tick 延迟，保存完成后没有新增崩溃或致命错误。

PID、在线人数和运行状态属于执行时快照，后续运维必须实时复核。结构化证据见
[`evidence/SKYREALM_PHANTOM_SPAWNING_DISABLED_2026-08-30.json`](evidence/SKYREALM_PHANTOM_SPAWNING_DISABLED_2026-08-30.json)。

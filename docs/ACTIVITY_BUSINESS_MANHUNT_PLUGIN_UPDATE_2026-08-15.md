# 赫朝商务追杀插件更新（2026-08-15）

## 更新范围

- 目标：owl5 固定活动槽 `activity`；
- 服务端目录：`E:\ActivityNeoForge`；
- 插件：`plugins\HechaoBusinessManhunt.jar`；
- 插件声明版本：`0.1.0`；
- 依赖：`HechaoAdminGame`；
- 更新前 SHA-256：
  `110714760ABBD37F8F33CFCBC770F27FA13FD996B3A0201742B53B0DB54DD819`；
- 更新后 SHA-256：
  `17B8DF20E63F54512D3DBC7F17D5DE5BFACC765ED6B4F24C5B9974B20A6B3B53`。

源文件大小为 `397,682` 字节，ZIP 结构、`plugin.yml`、插件主类和
`depend: [HechaoAdminGame]` 在上传前后均已验证。JAR 本体不进入 Git；仓库只保存
不含凭据的部署证据。

## 部署过程

维护前通过受管控制台确认在线人数为 `0/100`，执行 `save-all flush` 并在日志中确认
`Saved the game`，随后优雅停止 PID `1164`。旧 JAR 和插件数据目录备份到：

`C:\ProgramData\Hechao\backups\business-manhunt-plugin-pre-20260815T090700Z`

第一次原子替换把同盘回滚副本留成了 `plugins` 下的 `.jar` 文件。Paper 因而同时扫描
正式插件和回滚副本，报告 `Ambiguous plugin name 'HechaoBusinessManhunt'`。启动验收
立即判定失败，自动停止候选、恢复旧 JAR 与旧插件数据，并重新拉起旧版；旧版恢复后的
插件 SHA-256 与备份一致。

第二次维护再次确认零玩家、保存世界并优雅停止。回滚副本移到插件扫描目录外且使用
`.bak` 后缀，两个第一次产生的 Paper 重映射缓存移入正式备份目录；重新替换前确认根
插件目录只有一个 `HechaoBusinessManhunt` 身份。最终启动成功后，暂存目录、同盘临时
回滚文件和空维护目录均已清理，正式备份保留。

## 最终验收

- 活动任务：`Running`；
- 最终 Java PID：`1596`（易变运行快照）；
- Paper：出现 `Done`；
- `HechaoAdminGame`：正常启用；
- `HechaoBusinessManhunt`：只加载一次、只启用一次并输出 ready；
- 新插件文件 SHA-256：与输入制品一致；
- `bh status`：控制台冒烟通过，无未知命令或内部错误；
- `127.0.0.1:25568/TCP`：由 PID `1596` 唯一监听；
- `25578/UDP`：由 PID `1596` 监听，既有语音修复仍有效；
- 服控心跳：`activity / owl5 / 0.7.2 / wildcard=true / online=true / PID 1596`；
- 最终启动日志中商务追杀相关 `ERROR/SEVERE/Exception` 为 `0`；
- 根插件目录中 `HechaoBusinessManhunt` 身份计数为 `1`。

维护只操作固定活动槽。Velocity 继续由 PID `4644` 监听 `25577`，内部大厅继续由 PID
`7328` 监听 `25566`；独立生存槽使用自身 `25600` 端口，不属于本次变更。

## 回滚

回滚必须先确认活动服停止，再恢复备份目录中的旧 JAR 和
`HechaoBusinessManhunt` 数据目录，清理该插件在 `plugins\.paper-remapped` 下的缓存，
然后重新启动 `Hechao-Server-ActivityNeoForge`。恢复后必须重新核对旧 SHA-256、插件
ready、Paper `Done`、`25568/TCP` 和 `25578/UDP`。

## 剩余验收

本轮验证了加载、依赖、命令注册和网络监听，没有用真人账号开始完整追杀对局。正式录制
前仍需完成至少一次管理员 `bh start`、追杀者/逃亡者分配、追踪、死亡或结束、清理与
重开流程的多人验收。

机器可读证据见
[`evidence/ACTIVITY_BUSINESS_MANHUNT_PLUGIN_UPDATE_2026-08-15.json`](evidence/ACTIVITY_BUSINESS_MANHUNT_PLUGIN_UPDATE_2026-08-15.json)。

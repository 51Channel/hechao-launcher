# 赫朝商务追杀 Simple Voice Chat 生产修复

- 日期：`2026-08-15`
- 目标：owl5 固定活动替换槽 `activity`
- 服务端目录：`E:\ActivityNeoForge`
- 档案：`hechao-business-manhunt-paper-1.21.11 / 1.0.0`
- 变更状态：已在停服状态落盘并完成公网 UDP 探针；未启动 Minecraft

## 故障现象

玩家进入商务追杀后，Simple Voice Chat 显示红线划掉的插头。客户端日志确认：

- Fabric 客户端模组为 `voicechat-fabric-1.21.11-2.6.21.jar`；
- 服务端 Paper 插件为 `voicechat-bukkit-2.6.21.jar`；
- 客户端成功向后端请求并收到语音密钥；
- 随后持续尝试向 `43.226.63.164:24454` 认证，但没有收到 UDP 响应。

因此故障不在客户端模组缺失、版本不匹配或 Velocity 插件消息阶段，而在独立 UDP
语音链路。

## 根因

根因包含两层：

1. Paper 后端使用 `server-ip=127.0.0.1`，原语音配置的 `bind_address` 为空。
   Simple Voice Chat `2.6.21` 会在该值为空时继承 `server.properties` 的
   `server-ip`，因此语音端口只绑定回环地址。
2. owl5 是 NAT VPS。现有公网 UDP 映射不是内部端口原样直出；本次探针直接验证了
   `15156 -> 25577` 和 `15157 -> 25578`。在允许边缘穿越的临时诊断规则下，扫描
   `15150-15200/UDP` 仍没有任何端口转发到内部 `24454`；扫描空闲的
   `25577-25590/UDP` 也只命中上述两条既有映射。

Lobby 配置另声明 `owl5.vipi9.top:15158 -> 25579/UDP`，且本机存在对应 Java UDP
监听；为了不向在线 Lobby 注入无效数据，本次没有对 `15158` 做外部数据探针。

## 生产变更

在确认活动计划任务为 `Ready`、活动 Java 进程为 `0`、`25568/TCP`、`24454/UDP`
和 `25578/UDP` 均无活动占用后，修改：

`E:\ActivityNeoForge\plugins\voicechat\voicechat-server.properties`

```properties
port=25578
bind_address=*
voice_host=owl5.vipi9.top:15157
```

同时新增声明性 Windows 防火墙规则：

```text
MC-Activity-Voice-UDP
Inbound / Allow / UDP / LocalPort 25578 / Profile Any / EdgeTraversal Allow
```

owl5 的 Domain、Private、Public 防火墙配置文件当前都处于关闭状态，因此该规则不是本次
数据包能否到达的决定因素；保留它是为了将来启用 Windows 防火墙时不再次阻断活动语音。
旧 `MC-HorrorPrank-Voice-UDP / 24454` 规则没有删除，也没有改写其他服务端规则。

## 验证结果

- 客户端与服务端 Simple Voice Chat 版本均为 `2.6.21`；
- 服务端插件 SHA-256 保持
  `C08145770C19EC5550881B13C897E2A357E43EC419F7DA4039F73DCF279B94D8`；
- 最终语音配置 SHA-256 为
  `FCEAF7BAA6E9302ABCDF7D2D53AD204E9BBC90D2E5C6CE4400C8B80BE7AD4CF4`；
- 除 `port`、`bind_address` 和 `voice_host` 三个目标值外，配置内容保持一致；
- 从独立公网来源向 `owl5.vipi9.top:15157/UDP` 发送探针，owl5 内部
  `0.0.0.0:25578` 监听成功收到数据；
- 全部临时诊断防火墙规则已经删除，残留数为 `0`；
- 最终活动计划任务仍为 `Ready`，活动 Java、`25568/TCP` 和 `25578/UDP` 监听均为
  `0`，没有启动、停止或重启 Minecraft。

机器可读证据见
[`evidence/ACTIVITY_BUSINESS_MANHUNT_VOICECHAT_HOTFIX_2026-08-15.json`](evidence/ACTIVITY_BUSINESS_MANHUNT_VOICECHAT_HOTFIX_2026-08-15.json)。

## 回滚

正式回滚点：

```text
C:\ProgramData\Hechao\backups\activity-voicechat-route-20260815T074940Z
```

回滚只能在活动服停止、`25578/UDP` 无监听时执行：

1. 将目录策略保持 `Maintenance` 或 `Closed`；
2. 从回滚点恢复 `voicechat-server.properties.before`；
3. 复核恢复文件 SHA-256 为
   `96A62B15EC1E998F60131FB716EEB72860D9702A02D0D4E74B4D068CB22EBBD3`；
4. 仅在确认该规则没有被其他活动复用后删除 `MC-Activity-Voice-UDP`；
5. 回滚后保持停服，是否启动由管理员另行明确执行。

更早的绑定修复备份保留在
`C:\ProgramData\Hechao\backups\activity-voicechat-bind-20260815T073351Z`，但正常回滚应优先
使用上述最终路由变更回滚点。

## 剩余边界

- `25578/UDP` 与外部 `15157/UDP` 是 Survival2、DollNight 和旧活动服历史共用的语音槽。
  商务追杀占用固定活动替换槽期间，这些后端不能同时启用语音；启动前必须实时核验端口。
- 需要与 Survival2 或其他活动并行运行的独立槽，必须先取得独立公网 UDP 映射和独立
  内部端口，不能复制 `15157 -> 25578`。
- 本次是生产部署目录热修，原 `1.0.0` 服务端整合包未重发。重新部署原包前必须同步
  这三项配置，否则可能恢复为默认 `24454`。
- 下次由管理员手动启动商务追杀后，仍需核验日志显示 Voice Chat ready、实际
  `0.0.0.0:25578` 监听，并由真实玩家确认红线插头消失和双向语音正常。

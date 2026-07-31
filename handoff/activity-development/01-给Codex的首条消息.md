# 给 Codex 的首条消息

## 模式 A：当前只有规范

还没有活动名称、源码仓库或具体目标时，直接发送下面这段，不要填写假的占位值：

```text
这是赫朝未来 Minecraft 活动开发的规范接管任务。当前还没有活动名称、源码仓库、
玩法需求或上线日期，本次不要创建虚构项目，也不要连接、启动或修改生产服务器。

请完整读取本交接包根目录 AGENTS.md、00-从这里开始.md、
03-如何基于现有框架开发.md、docs/ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md、
docs/HECHAO_NEW_SERVER_BASELINE.md、docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md 和
docs/examples/server-baseline/component-plan.example.json。

阅读后请输出：
1. 你理解的活动通道、物理活动槽、客户端档案、目录记录和一次性授权链路；
2. Velocity 单例、内部大厅专用、VPS 主机级、后端条件必需和活动自有组件的区别；
3. 为什么不能复制大厅、Survival 或旧活动服的 plugins/mods/config；
4. 未来拿到真实活动时，你需要负责人提供和需要自己实时核验的信息；
5. 从需求、组件计划、开发、测试、签名档案、停服部署到灰度和回滚的执行顺序。

此阶段只做规范理解和准备清单。不要要求我现在提供并不存在的源码或名称，不要把样例
注册到生产，也不要把 forwarding、指标或大厅组件的缺口靠猜测补齐。
```

## 模式 B：开始实际活动开发

把尖括号内容替换为本次真实信息。没有值的字段写“待实时核验”，不要凭记忆填写。

```text
这是赫朝 Minecraft 活动开发任务。

活动名称：<活动显示名>
任务类型：<新活动 / 现有活动修复 / 客户端更新 / 服务端更新 / 地图更新>
活动源码仓库：<绝对路径或 Git 地址>
本次目标：<一句话说明玩家可观察到的结果>
目标人数：<例如 20>
计划活动日期：<日期或待定>
生产权限：<仅开发 / 允许测试档案 / 允许部署但保持停服 / 明确允许生产启动>

先完整读取本交接包根目录 AGENTS.md、00-从这里开始.md、
03-如何基于现有框架开发.md、docs/ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md 和
docs/HECHAO_NEW_SERVER_BASELINE.md、docs/examples/ACTIVITY_CHANNEL_MINIMAL_CASE.md。
再进入实际源码仓库读取其 AGENTS.md、Git 状态、构建文件和现有测试。

所有玩家活动统一使用 velocityTarget=activity；当前活动物理槽是 owl5
127.0.0.1:25568 和 owl5-activity-slot，同一时刻只允许一个后端。不得复用
survival2、lobby 或 pvp，不得增加 /hub、大厅 NPC 或失败回退。

请先通过实时文件与只读状态确认：Minecraft/加载器/Java、活动 ID、serverId、
controlTargetId、profileId、当前客户端清单、服务端目录、计划任务、25568 监听、
冲突组、基础组件计划、forwarding、指标实现、世界备份和回滚目标。不要要求我重复
交接包中已经写明且能够核验的信息。

实现时保持服务端权威；客户端包只表达意图。Minecraft 世界 API 不放到异步线程。
不要通过缩小半径、减少人数或改变玩法规则掩盖卡顿。部署默认保持停服，只有上面的
生产权限明确允许时才能启动。

先给出你确认到的当前事实、仍待核验项和最小变更范围，然后持续完成源码、测试、
打包、文档、Git 和已授权的部署动作。最终按 05-最终交付报告模板.md 报告真实结果。
```

## 修复任务补充

如果是修复，把下面内容追加到消息：

```text
复现步骤：<步骤>
期望行为：<期望>
实际行为：<实际>
最近正常版本：<版本或未知>
诊断文件：<路径或无>
不得改变的玩法规则：<例如半径、人数、角色比例>
```

## 新活动补充

如果是新活动，把下面内容追加到消息：

```text
请先填写 02-新活动需求单模板.md，并为新活动选择稳定 activityId、serverId、
controlTargetId 和 profileId。正常新增活动不修改启动器 UI；通过签名档案和后台目录
接入。任何新物理后端必须先完成基础组件计划，再加入 owl5-activity-slot、状态采集、
指标、备份和告警。
```

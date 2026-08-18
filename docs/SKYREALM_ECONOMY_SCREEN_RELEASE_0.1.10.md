# 天域远征工业季第三方屏幕 0.1.10 Test 发布

- 发布日期：2026-08-18
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.1.10.jar`
- JAR 大小：`841,439` 字节
- JAR SHA-256：
  `02FC9FE719A103AAC1FCC0270560D2DD19EC9C51B8E12BD8017E08E35A5A2468`
- 网络协议：`2`
- 功能源码提交：`9bae82d`
- 客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.19 / Test=100%`

`0.1.10` 使用中转站 `gpt-image-2` 生成的工业机械背景和齿轮罗盘徽记，统一改版导航、
余额、出售结果和回收目录。背景以暗色铁板、铜管、黄铜结构和少量青绿色仪表为主，中心
保持低细节；前景面板、商品卡片、状态轨和按钮使用同一套金属边框与状态色。背景按
`cover` 语义居中裁切，窄屏、超宽和极矮窗口均有确定布局。

Image2 完整提示词、原始生成文件摘要、最终资源尺寸与 SHA-256 记录在
[`SKYREALM_ECONOMY_SCREEN_RELEASE_0.1.10_CANDIDATE.md`](SKYREALM_ECONOMY_SCREEN_RELEASE_0.1.10_CANDIDATE.md)。
项目只保留最终游戏资源，没有保存认证材料或生成中间文件。

余额、出售确认、85 项回收目录、商品分页、固定动作、短期会话、服务端授权、价格、网络
载荷和协议版本均未改变。Gradle `clean test build` 连续两次通过，`32/32` 测试通过；
两次清理后重建的 JAR 大小和 SHA-256 一致。客户端档案 `1.0.19` 已发布到
`Test=r12 / 100%`，Gray 与 Production 保持未分配。

本次没有重启 API、Minecraft、Velocity、代理或服控进程。玩家必须完全退出旧游戏，再由
启动器增量更新后验收导航、余额、出售和回收目录的构图、文字清晰度、按钮点击与分页；
真人视觉和交互验收完成前不得推进 Gray 或 Production。


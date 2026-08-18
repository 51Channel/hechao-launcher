# 天域远征工业季第三方屏幕 0.1.8 Test 发布

- 发布日期：2026-08-18
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.1.8.jar`
- JAR 大小：`52,098` 字节
- JAR SHA-256：
  `61D523AE1D546E72C2B679074F89983B978C29DA6CBD9C2A11AF9A983A32210E`
- 网络协议：`2`
- 功能源码提交：`f42eded`
- 客户端档案：`skyrealm-industrial-neoforge-1.21.1 / 1.0.17 / Test=100%`

`0.1.7` 的余额结果页、出售报价页和回收目录先调用 `renderBackground`，绘制自定义内容后
又调用 `Screen.render`。Minecraft `1.21.1` 的 `Screen.render` 会再次执行背景模糊，
因此面板、标题和业务文字被二次模糊，而最后绘制的原版按钮仍然清晰。

`0.1.8` 将三个页面统一到不可被覆盖的单次背景渲染管线：背景只模糊一次，随后依次绘制
自定义内容、原版控件和 Tooltip。余额、出售确认、商品目录、按钮命令、服务端授权和网络
协议均未改变。生产商品表仍为 `0` 条，没有导入 `85` 项待审核候选。

Gradle `clean test build` 连续两次通过，`24/24` 测试通过；两次清理后重建的 JAR 大小和
SHA-256 一致。客户端档案 `1.0.17` 已发布到 `Test=r10 / 100%`；Gray 与 Production
保持未分配。没有重启 API、Minecraft、Velocity、代理或服控进程。

玩家必须完全退出旧游戏，再由启动器增量更新后验收三个页面的文字、面板和 Tooltip。
完成真人清晰度验证前不得推进 Gray 或 Production。

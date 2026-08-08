# 赫朝启动器产品事实

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

主要用户是通过赫朝 Minecraft 社区参加长期生存、活动录制与特别企划的 Windows 玩家。管理员使用独立的 Web 管理后台，不在玩家主页执行服务器管理操作。

## Product Purpose

赫朝启动器统一完成赫朝账号登录、Microsoft/Minecraft 正版身份绑定、权限过滤、服务器选择、客户端档案安装更新、兼容 Java 准备与游戏启动。玩家只通过启动器选择和切换服务器。

## Positioning

服务器目录、玩家等级、开放状态、客户端版本和活动排期来自赫朝平台的同一权威数据链；启动器根据真实状态自动准备正确的隔离客户端，而不是让玩家手工维护多个整合包。

## Operating Context

- Windows 安装式 WPF 桌面应用。
- 每个客户端档案使用独立 `.minecraft` 和受管 Java，同时共享玩家常用设置。
- Velocity 保留为内部统一入口和授权层；大厅只承载前置能力，不向玩家开放。
- 活动页和官网使用同一活动排期 API。

## Capabilities and Constraints

- 主页只能显示目录返回的真实服务器、状态、人数、公告和活动排期。
- 不伪造延迟、人数、公告、收藏状态或活动。
- 安装、修复、回滚、删除客户端和每档案 Java 设置必须保持可达。
- 长任务期间必须冻结会破坏任务上下文的服务器选择。
- 不在玩家启动器中提供添加、编辑、启停或删除服务端能力。
- 生产发布、服务端启停和代理操作是独立的受控流程，UI 改版不得隐式触发。

## Brand Commitments

产品名称为“赫朝启动器”。沿用赫朝红、现有品牌图标、IconPark 官方图标和苹方优先字体。Minecraft、Mojang 和 Microsoft 的非官方关系声明保持不变。

## Evidence on Hand

- 现有品牌与服务器横幅：`src/Hechao.Launcher/Assets/`。
- 当前功能和发布状态：`README.md`、`docs/COMPLETION_MATRIX.md`。
- 前端与业务审查：`docs/LAUNCHER_FRONTEND_AUDIT_2026-08-01.md`。
- 用户提供的本次主页参考图只约束布局方向，不证明其中示例数据或功能存在。

## Product Principles

1. 服务器和客户端状态始终以真实权威数据为准。
2. 让玩家在一个清晰主流程中完成选服、准备客户端和启动游戏。
3. 高风险或低频能力保留明确入口，但不压过主页主任务。
4. 网络失败、空数据和任务进行中都必须给出可恢复状态。
5. 玩家设置、客户端数据和账号凭据在更新与改版中保持安全。

## Accessibility & Inclusion

保持键盘可达、稳定 UI Automation 名称、清晰焦点反馈、可缩放文本和最小窗口可用性；公开分发前仍需人工验证 Narrator 与常见 DPI。

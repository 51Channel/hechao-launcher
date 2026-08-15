# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Stack

- 现有 Windows 客户端：.NET 10 + WPF。
- macOS 客户端：.NET 10 + Avalonia，目标运行时仅为 `osx-arm64`。
- 服务器目录、账号、签名分发和 Minecraft 启动能力继续使用现有 C# 合同与服务，不另建不兼容协议。

## Users

- 主要用户是使用 M4 Mac、通过赫朝社区游玩正版 Minecraft Java 模组客户端的玩家。
- 用户在一个启动器内完成赫朝登录、Microsoft/Minecraft 正版绑定、服务器选择、整合包准备和进入游戏，不需要手工拼装模组目录或 Java 环境。

## Product Purpose

赫朝启动器是社区唯一的服务器选择与客户端交付入口。macOS 版本需要提供可实际使用的完整玩家路径，而不是仅展示现有界面的原型。

成功意味着 M4 Mac 玩家能够安装应用、登录、下载并校验目标客户端、准备 ARM64 Java、启动 Minecraft、查看运行状态和诊断失败，同时不改变现有服务器授权与分发边界。

## Positioning

启动器把赫朝账号、Minecraft 正版身份、服务器权限、活动排期、签名整合包和 Velocity 进服授权连成同一条受控路径；普通通用启动器无法复制这组社区端到端契约。

## Operating Context

- 玩家主要在 macOS 桌面环境中反复使用服务器主页、活动页、下载状态与快捷设置。
- 不同 Minecraft 版本、加载器和独立模组集合必须使用隔离客户端档案和可写 `.minecraft`。
- 服务端/API 是服务器可见性与进服授权的权威；客户端只呈现并执行经过签名和授权的结果。
- Windows 与 macOS 客户端需要并行维护，不能用 macOS 适配破坏正式 WPF 客户端。

## Capabilities and Constraints

- 首版必须包含真实赫朝登录与会话恢复、Microsoft/Minecraft 正版绑定、皮肤与账户状态。
- 必须包含服务器和活动目录、签名清单校验、断点续传、原子安装、修复与删除客户端档案。
- 必须发现或准备原生 ARM64 Java，并以独立档案启动和停止 Minecraft Java。
- 必须提供设置、运行进度、失败恢复和脱敏诊断；不得保存或输出密码、令牌、Cookie 或身份材料。
- macOS 目标只支持 M4 等 Apple Silicon，不交付 Intel/x64 或 Rosetta 版本。
- 正式公开发布需要 Apple Developer ID 签名和公证；当前未提供签名身份，因此不得宣称未签名测试包已经公证。
- Windows 专属的 DPAPI、注册表、资源管理器、安装器和自更新实现必须由 macOS 平台实现替代。

## Brand Commitments

- 产品名称保持“赫朝启动器”，延续现有图标、堡垒横幅、中文文案与安静、工具化的视觉体系。
- 赫朝启动器是独立社区产品，非 Minecraft 官方产品，未经 Mojang 或 Microsoft 批准，也不与其关联。
- macOS 版本应遵循平台原生窗口、菜单、键盘与辅助功能预期，但不改变产品身份。

## Evidence on Hand

- Windows 正式客户端源码与真实业务逻辑：`src/Hechao.Launcher`。
- 协议与分发合同：`src/Hechao.Contracts`、`src/Hechao.Distribution`。
- 正式视觉资产：`src/Hechao.Launcher/Assets`。
- Windows 版自动化测试：`tests/Hechao.Launcher.Tests`。
- 生产发布与行为证据：`README.md` 和 `docs/LAUNCHER_RELEASE_0.15.8.md`。
- 当前没有 Apple Developer ID、notarytool 凭据或 M4 实机验收记录；后续不得虚构这些证据。

## Product Principles

1. 真实玩家路径优先于界面演示。
2. 复用现有权威合同，平台差异隔离在明确的服务边界。
3. 下载、身份和启动默认故障关闭，失败时保留可恢复状态。
4. 模组档案彼此隔离，共享对象只按已验证摘要复用。
5. Windows 正式客户端与生产服务不因 macOS 开发发生隐式变更。

## Accessibility & Inclusion

- 所有主要操作必须支持键盘导航、清晰焦点、屏幕阅读器名称和不依赖颜色的状态表达。
- 界面需要在 macOS 缩放、长中文文本和窄窗口下保持可操作，不允许按钮文字裁切或内容重叠。
